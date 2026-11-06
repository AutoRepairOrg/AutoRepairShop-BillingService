using BillingService.Application.Interfaces;
using BillingService.Domain.Events;
using Microsoft.Extensions.Logging;

namespace BillingService.Infrastructure.Messaging;

/// <summary>
/// Publisher fake para desenvolvimento local sem RabbitMQ.
/// Ativado via appsettings: "Messaging:UseStub": true
/// </summary>
public class StubEventPublisher(ILogger<StubEventPublisher> logger) : IEventPublisher
{
    public Task PublishAsync(DomainEvent @event, CancellationToken ct = default)
    {
        var eventType = @event.GetType().Name;
        RabbitMqSagaActivity.RecordPublished(eventType);
        logger.LogInformation(
            "Saga event published {event_type} {aggregate_id} {correlation_id}",
            eventType,
            @event.AggregateId,
            RabbitMqSagaActivity.Current);

        return Task.CompletedTask;
    }
}
