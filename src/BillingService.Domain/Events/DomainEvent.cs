namespace BillingService.Domain.Events;

public abstract record DomainEvent(Guid AggregateId, DateTime OccurredAt)
{
    protected DomainEvent(Guid aggregateId) : this(aggregateId, DateTime.UtcNow) { }
}

public record BudgetGeneratedEvent(Guid BudgetId, Guid OsId, decimal Total)
    : DomainEvent(BudgetId);

public record BudgetApprovedEvent(Guid BudgetId, Guid OsId)
    : DomainEvent(BudgetId);

public record BudgetRejectedEvent(Guid BudgetId, Guid OsId, string Reason)
    : DomainEvent(BudgetId);

public record PaymentConfirmedEvent(Guid PaymentId, Guid OsId, decimal Amount)
    : DomainEvent(PaymentId);

public record PaymentFailedEvent(Guid PaymentId, Guid OsId, string Reason)
    : DomainEvent(PaymentId);
