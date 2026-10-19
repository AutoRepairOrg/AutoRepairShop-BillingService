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
/// Consome eventos dos demais serviços (Saga coreografada).
///
/// Exchanges / filas:
///   os-service-events          → billing-service-os-events
///     OsCreatedEvent, OsSentForApprovalEvent, OsCancelledEvent
///   execution-service-events   → billing-service-execution-events
///     ExecutionCompletedEvent
/// </summary>
public class RabbitMqConsumerService(
    IConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<RabbitMqConsumerService> logger) : BackgroundService
{
    private const string OsExchange   = "os-service-events";
    private const string OsQueue      = "billing-service-os-events";
    private const string ExecExchange = "execution-service-events";
    private const string ExecQueue    = "billing-service-execution-events";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await DeclareAndBindAsync(
            OsExchange, OsQueue,
            ["OsCreatedEvent", "OsSentForApprovalEvent", "OsCancelledEvent"],
            stoppingToken);

        await DeclareAndBindAsync(
            ExecExchange, ExecQueue,
            ["ExecutionCompletedEvent"],
            stoppingToken);

        await _channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        await _channel.BasicConsumeAsync(OsQueue,   autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        await _channel.BasicConsumeAsync(ExecQueue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        logger.LogInformation(
            "BillingService RabbitMQ consumer iniciado — queues {OsQueue}, {ExecQueue}",
            OsQueue, ExecQueue);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown normal
        }
    }

    private async Task DeclareAndBindAsync(
        string exchange, string queue, string[] routingKeys, CancellationToken ct)
    {
        await _channel!.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await _channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);

        foreach (var key in routingKeys)
            await _channel.QueueBindAsync(queue, exchange, key, cancellationToken: ct);
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
                case "OsSentForApprovalEvent":
                {
                    var msg = JsonSerializer.Deserialize<IncomingOsSentForApproval>(body, JsonOptions)
                        ?? throw new InvalidOperationException("Payload OsSentForApprovalEvent inválido.");

                    var handler = scope.ServiceProvider.GetRequiredService<OsSentForApprovalHandler>();
                    await handler.HandleAsync(msg.OsId, msg.Items);
                    break;
                }
                case "OsCancelledEvent":
                {
                    var msg = JsonSerializer.Deserialize<IncomingOsCancelled>(body, JsonOptions)
                        ?? throw new InvalidOperationException("Payload OsCancelledEvent inválido.");

                    var handler = scope.ServiceProvider.GetRequiredService<OsCancelledHandler>();
                    await handler.HandleAsync(msg.OsId, msg.Reason);
                    break;
                }
                case "ExecutionCompletedEvent":
                {
                    var msg = JsonSerializer.Deserialize<IncomingExecutionCompleted>(body, JsonOptions)
                        ?? throw new InvalidOperationException("Payload ExecutionCompletedEvent inválido.");

                    var handler = scope.ServiceProvider.GetRequiredService<ExecutionCompletedHandler>();
                    await handler.HandleAsync(msg.OsId);
                    break;
                }
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

    // DTOs mínimos para deserializar eventos publicados pelos outros serviços
    // (sem acoplar os Domain projects)
    private sealed record IncomingOsCreated(Guid OsId, string CustomerName, string VehiclePlate);
    private sealed record IncomingOsSentForApproval(Guid OsId, List<IncomingOsItem> Items);
    private sealed record IncomingOsCancelled(Guid OsId, string Reason);
    private sealed record IncomingExecutionCompleted(Guid OsId);
}
