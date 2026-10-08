using BillingService.Application.DTOs;
using BillingService.Application.Interfaces;
using BillingService.Domain.Entities.Budget;
using BillingService.Domain.Interfaces;

namespace BillingService.Application.Services;

public class BudgetAppService(
    IBudgetRepository repository,
    IEventPublisher eventPublisher)
{
    public async Task<BudgetResponse> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var budget = await repository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Orçamento {id} não encontrado.");

        return MapToResponse(budget);
    }

    public async Task<BudgetResponse> GetByOsIdAsync(Guid osId, CancellationToken ct = default)
    {
        var budget = await repository.GetByOsIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"Orçamento para OS {osId} não encontrado.");

        return MapToResponse(budget);
    }

    public async Task<BudgetResponse> AddItemAsync(Guid id, AddBudgetItemRequest request, CancellationToken ct = default)
    {
        var budget = await GetOrThrowAsync(id, ct);
        budget.AddItem(request.Description, request.Price, request.Quantity);
        await repository.UpdateAsync(budget, ct);
        return MapToResponse(budget);
    }

    public async Task<BudgetResponse> SendAsync(Guid id, CancellationToken ct = default)
    {
        var budget = await GetOrThrowAsync(id, ct);
        budget.Send();
        await repository.UpdateAsync(budget, ct);
        await PublishDomainEventsAsync(budget, ct);
        return MapToResponse(budget);
    }

    public async Task<BudgetResponse> ApproveAsync(Guid id, CancellationToken ct = default)
    {
        var budget = await GetOrThrowAsync(id, ct);
        budget.Approve();
        await repository.UpdateAsync(budget, ct);
        await PublishDomainEventsAsync(budget, ct);
        return MapToResponse(budget);
    }

    public async Task<BudgetResponse> RejectAsync(Guid id, string reason, CancellationToken ct = default)
    {
        var budget = await GetOrThrowAsync(id, ct);
        budget.Reject(reason);
        await repository.UpdateAsync(budget, ct);
        await PublishDomainEventsAsync(budget, ct);
        return MapToResponse(budget);
    }

    private async Task<Budget> GetOrThrowAsync(Guid id, CancellationToken ct)
        => await repository.GetByIdAsync(id, ct)
           ?? throw new KeyNotFoundException($"Orçamento {id} não encontrado.");

    private async Task PublishDomainEventsAsync(Budget budget, CancellationToken ct)
    {
        foreach (var @event in budget.DomainEvents)
            await eventPublisher.PublishAsync(@event, ct);

        budget.ClearDomainEvents();
    }

    private static BudgetResponse MapToResponse(Budget budget) => new(
        budget.Id,
        budget.OsId,
        budget.CustomerName,
        budget.VehiclePlate,
        budget.Status.ToString(),
        budget.CalculateTotal(),
        budget.CreatedAt,
        budget.UpdatedAt,
        budget.RejectionReason,
        budget.Items.Select(i => new BudgetItemResponse(i.Id, i.Description, i.Price, i.Quantity, i.Subtotal))
    );
}
