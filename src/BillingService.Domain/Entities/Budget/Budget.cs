using BillingService.Domain.Enums;
using BillingService.Domain.Events;

namespace BillingService.Domain.Entities.Budget;

/// <summary>
/// Aggregate root do Orçamento.
/// Parte do Saga coreografado — gera, aprova ou rejeita orçamentos de OS.
/// </summary>
public class Budget
{
    public Guid Id { get; private set; }
    public Guid OsId { get; private set; }
    public string CustomerName { get; private set; }
    public string VehiclePlate { get; private set; }
    public BudgetStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    private readonly List<BudgetItem> _items = [];
    private readonly List<DomainEvent> _domainEvents = [];

    public IReadOnlyCollection<BudgetItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

#pragma warning disable CS8618
    protected Budget() { }
#pragma warning restore CS8618

    private Budget(Guid osId, string customerName, string vehiclePlate)
    {
        Id = Guid.NewGuid();
        OsId = osId;
        CustomerName = customerName;
        VehiclePlate = vehiclePlate.ToUpperInvariant();
        Status = BudgetStatus.Draft;
        CreatedAt = DateTime.UtcNow;
    }

    public static Budget Create(Guid osId, string customerName, string vehiclePlate)
    {
        if (osId == Guid.Empty) throw new ArgumentException("OsId é obrigatório.", nameof(osId));
        ArgumentException.ThrowIfNullOrWhiteSpace(customerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(vehiclePlate);

        return new Budget(osId, customerName, vehiclePlate);
    }

    public void AddItem(string description, decimal price, int quantity = 1)
    {
        if (Status is not BudgetStatus.Draft)
            throw new InvalidOperationException("Itens só podem ser adicionados em orçamentos Draft.");

        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (price <= 0) throw new ArgumentOutOfRangeException(nameof(price), "Preço deve ser maior que zero.");
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantidade deve ser maior que zero.");

        _items.Add(new BudgetItem(Id, description, price, quantity));
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Envia o orçamento ao cliente (Draft → Sent) e publica BudgetGeneratedEvent.
    /// </summary>
    public void Send()
    {
        EnsureStatus(BudgetStatus.Draft, "Somente orçamentos Draft podem ser enviados.");
        if (_items.Count == 0)
            throw new InvalidOperationException("Orçamento precisa ter ao menos um item antes do envio.");

        Status = BudgetStatus.Sent;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new BudgetGeneratedEvent(Id, OsId, CalculateTotal()));
    }

    public void Approve()
    {
        EnsureStatus(BudgetStatus.Sent, "Somente orçamentos Sent podem ser aprovados.");
        Status = BudgetStatus.Approved;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new BudgetApprovedEvent(Id, OsId));
    }

    public void Reject(string reason)
    {
        EnsureStatus(BudgetStatus.Sent, "Somente orçamentos Sent podem ser rejeitados.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Status = BudgetStatus.Rejected;
        RejectionReason = reason;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new BudgetRejectedEvent(Id, OsId, reason));
    }

    public decimal CalculateTotal() => _items.Sum(i => i.Subtotal);

    public void ClearDomainEvents() => _domainEvents.Clear();

    private void EnsureStatus(BudgetStatus expected, string message)
    {
        if (Status != expected)
            throw new InvalidOperationException(message);
    }

    private void AddDomainEvent(DomainEvent @event) => _domainEvents.Add(@event);
}
