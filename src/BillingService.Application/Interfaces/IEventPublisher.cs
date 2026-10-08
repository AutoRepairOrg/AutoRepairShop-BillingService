using BillingService.Domain.Events;

namespace BillingService.Application.Interfaces;

public interface IEventPublisher
{
    Task PublishAsync(DomainEvent @event, CancellationToken ct = default);
}
