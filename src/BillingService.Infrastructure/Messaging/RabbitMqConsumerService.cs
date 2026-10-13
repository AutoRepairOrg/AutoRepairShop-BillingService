using System.Text;
using System.Text.Json;
using BillingService.Application.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BillingService.Infrastructure.Messaging;

/// <summary>
/// Consome eventos do OsService (Saga coreografada).
/// Exchange: os-service-events → Queue: billing-service-os-events
/// </summary>
public class RabbitMqConsumerService(
    IConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<RabbitMqConsumerService> logger) : BackgroundService
{
    private const string ExchangeName = "os-service-events";
    private const string QueueName = "billing-service-os-events";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await _channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(QueueName, ExchangeName, "OsCreatedEvent", cancellationToken: stoppingToken);
        await _channel.QueueBindAsync(QueueName, ExchangeName, "OsCancelledEvent", cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        await _channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation(
            "BillingService RabbitMQ consumer iniciado — queue {Queue}, exchange {Exchange}",
            QueueName, ExchangeName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown normal
        }
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var routingKey = ea.RoutingKey;
        var body = Encoding.UTF8.GetString(ea.Body.ToArray());

        try
        {
            using var scope = scopeFactory.CreateScope();

            switch (routingKey)
            {
                case "OsCreatedEvent":
                {
                    var msg = JsonSerializer.Deserialize<IncomingOsCreated>(body, JsonOptions)
                        ?? throw new InvalidOperationException("Payload OsCreatedEvent inválido.");

                    var handler = scope.ServiceProvider.GetRequiredService<OsCreatedHandler>();
                    await handler.HandleAsync(msg.OsId, msg.CustomerName, msg.VehiclePlate);
                    break;
                }
                case "OsCancelledEvent":
                    // Compensação futura — por ora apenas loga
                    logger.LogInformation("OsCancelledEvent recebido (sem ação): {Body}", body);
                    break;
                default:
                    logger.LogWarning("Routing key ignorada: {RoutingKey}", routingKey);
                    break;
            }

            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
            logger.LogInformation("Evento {RoutingKey} processado com sucesso", routingKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao processar {RoutingKey}: {Body}", routingKey, body);
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
            await _channel.DisposeAsync();

        await base.StopAsync(cancellationToken);
    }

    // DTO mínimo para deserializar evento publicado pelo OsService (sem acoplar os Domain projects)
    private sealed record IncomingOsCreated(Guid OsId, string CustomerName, string VehiclePlate);
}
