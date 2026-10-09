using BillingService.Domain.Entities.Budget;
using BillingService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BillingService.Infrastructure.Persistence;

public class BudgetRepository(BillingDbContext db) : IBudgetRepository
{
    public async Task AddAsync(Budget budget, CancellationToken ct = default)
    {
        await db.Budgets.AddAsync(budget, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Budget budget, CancellationToken ct = default)
    {
        db.Budgets.Update(budget);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddItemAsync(Budget budget, CancellationToken ct = default)
    {
        foreach (var item in budget.Items)
        {
            var entry = db.Entry(item);
            entry.State = EntityState.Added;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Budgets
            .Include("_items")
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<Budget?> GetByOsIdAsync(Guid osId, CancellationToken ct = default)
        => await db.Budgets
            .Include("_items")
            .FirstOrDefaultAsync(b => b.OsId == osId, ct);
}
