using BillingService.Domain.Enums;
using BillingService.Domain.Events;

namespace BillingService.Domain.Entities.Payment;

/// <summary>
/// Aggregate root do Pagamento.
/// Integrado ao Mercado Pago (Checkout Pro) via webhook.
/// </summary>
public class Payment
{
    public Guid Id { get; private set; }
    public Guid OsId { get; private set; }
    public Guid BudgetId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? MercadoPagoPaymentId { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private readonly List<DomainEvent> _domainEvents = [];
    public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

#pragma warning disable CS8618
    protected Payment() { }
#pragma warning restore CS8618

    private Payment(Guid osId, Guid budgetId, decimal amount)
    {
        Id = Guid.NewGuid();
        OsId = osId;
        BudgetId = budgetId;
        Amount = amount;
        Status = PaymentStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public static Payment Create(Guid osId, Guid budgetId, decimal amount)
    {
        if (osId == Guid.Empty) throw new ArgumentException("OsId é obrigatório.", nameof(osId));
        if (budgetId == Guid.Empty) throw new ArgumentException("BudgetId é obrigatório.", nameof(budgetId));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Valor deve ser maior que zero.");

        return new Payment(osId, budgetId, amount);
    }

    public void SetCheckoutUrl(string checkoutUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkoutUrl);
        CheckoutUrl = checkoutUrl;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Confirm(string mpPaymentId)
    {
        EnsureStatus(PaymentStatus.Pending, "Somente pagamentos Pending podem ser confirmados.");
        ArgumentException.ThrowIfNullOrWhiteSpace(mpPaymentId);

        Status = PaymentStatus.Confirmed;
        MercadoPagoPaymentId = mpPaymentId;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new PaymentConfirmedEvent(Id, OsId, Amount));
    }

    public void Fail(string reason)
    {
        EnsureStatus(PaymentStatus.Pending, "Somente pagamentos Pending podem falhar.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Status = PaymentStatus.Failed;
        FailureReason = reason;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new PaymentFailedEvent(Id, OsId, reason));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void EnsureStatus(PaymentStatus expected, string message)
    {
        if (Status != expected)
            throw new InvalidOperationException(message);
    }

    private void AddDomainEvent(DomainEvent @event) => _domainEvents.Add(@event);
}
