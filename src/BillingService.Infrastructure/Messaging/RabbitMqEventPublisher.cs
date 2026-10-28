using System.Text;
using System.Text.Json;
using BillingService.Application.Interfaces;
using BillingService.Domain.Events;
using RabbitMQ.Client;

namespace BillingService.Infrastructure.Messaging;

/// <summary>
/// Implementação do IEventPublisher usando RabbitMQ.
/// </summary>
public class RabbitMqEventPublisher(IConnection connection) : IEventPublisher, IAsyncDisposable, IDisposable
{
    private IChannel? _channel;

    private async Task<IChannel> GetChannelAsync()
    {
        if (_channel is null || !_channel.IsOpen)
            _channel = await connection.CreateChannelAsync();
        return _channel;
    }

    public async Task PublishAsync(DomainEvent @event, CancellationToken ct = default)
    {
        var channel = await GetChannelAsync();
        var exchangeName = "billing-service-events";
        var routingKey = @event.GetType().Name;

        await channel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: ct
        );

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event, @event.GetType()));

        await channel.BasicPublishAsync(
            exchange: exchangeName,
            routingKey: routingKey,
            body: body,
            cancellationToken: ct
        );
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
        _channel = null;
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
