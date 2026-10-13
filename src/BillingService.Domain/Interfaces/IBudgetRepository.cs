using BillingService.Domain.Entities.Budget;

namespace BillingService.Domain.Interfaces;

public interface IBudgetRepository
{
    Task AddAsync(Budget budget, CancellationToken ct = default);
    Task UpdateAsync(Budget budget, CancellationToken ct = default);
    Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Budget?> GetByOsIdAsync(Guid osId, CancellationToken ct = default);
    Task AddItemAsync(Budget budget, CancellationToken ct = default);
}
