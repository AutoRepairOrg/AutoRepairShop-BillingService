namespace BillingService.Domain.Entities.Budget;

public class BudgetItem
{
    public Guid Id { get; private set; }
    public Guid BudgetId { get; private set; }
    public string Description { get; private set; }
    public decimal Price { get; private set; }
    public int Quantity { get; private set; }

#pragma warning disable CS8618
    protected BudgetItem() { }
#pragma warning restore CS8618

    internal BudgetItem(Guid budgetId, string description, decimal price, int quantity)
    {
        Id = Guid.NewGuid();
        BudgetId = budgetId;
        Description = description;
        Price = price;
        Quantity = quantity;
    }

    public decimal Subtotal => Price * Quantity;
}
