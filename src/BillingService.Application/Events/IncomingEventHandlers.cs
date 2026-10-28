using BillingService.Application.Interfaces;
using BillingService.Domain.Entities.Budget;
using BillingService.Domain.Entities.Payment;
using BillingService.Domain.Enums;
using BillingService.Domain.Interfaces;

namespace BillingService.Application.Events;

/// <summary>
/// Handlers para eventos consumidos do broker (Saga coreografada).
/// </summary>
public class OsCreatedHandler(IBudgetRepository budgetRepository)
{
    /// <summary>
    /// Consumido quando OsService cria uma OS.
    /// Cria orçamento em Draft para a OS.
    /// </summary>
    public async Task HandleAsync(Guid osId, string customerName, string vehiclePlate, CancellationToken ct = default)
    {
        var existing = await budgetRepository.GetByOsIdAsync(osId, ct);
        if (existing is not null)
            return;

        var budget = Budget.Create(osId, customerName, vehiclePlate);
        await budgetRepository.AddAsync(budget, ct);
    }
}

/// <summary>
/// Item diagnosticado na OS, recebido via OsSentForApprovalEvent.
/// Espelha OsItemPayload do OsService sem acoplar os Domain projects.
/// </summary>
public record IncomingOsItem(string ServiceName, decimal Price, int Quantity);

public class OsSentForApprovalHandler(IBudgetRepository budgetRepository)
{
    /// <summary>
    /// Consumido quando OsService envia a OS para aprovação, já com itens diagnosticados.
    /// Cria o Budget (se ainda não existir) e popula os itens. Não depende de OsCreatedEvent.
    /// Idempotente: se o Budget já tiver itens, não adiciona novamente.
    /// </summary>
    public async Task HandleAsync(
        Guid osId,
        string customerName,
        string vehiclePlate,
        IReadOnlyList<IncomingOsItem> items,
        CancellationToken ct = default)
    {
        if (items is null || items.Count == 0)
            throw new InvalidOperationException($"OsSentForApproval da OS {osId} chegou sem itens.");

        var budget = await budgetRepository.GetByOsIdAsync(osId, ct);
        if (budget is null)
        {
            budget = Budget.Create(osId, customerName, vehiclePlate);
            foreach (var item in items)
                budget.AddItem(item.ServiceName, item.Price, item.Quantity);

            await budgetRepository.AddAsync(budget, ct);
            return;
        }

        if (budget.Items.Count > 0)
            return;

        foreach (var item in items)
            budget.AddItem(item.ServiceName, item.Price, item.Quantity);

        await budgetRepository.AddItemAsync(budget, ct);
    }
}

public class OsCancelledHandler(
    IBudgetRepository budgetRepository,
    IPaymentRepository paymentRepository)
{
    /// <summary>
    /// Compensação do Saga: OS cancelada → cancela Budget e Payment pendentes.
    /// Não republica BudgetRejectedEvent para evitar loop com o OsService.
    /// </summary>
    public async Task HandleAsync(Guid osId, string reason, CancellationToken ct = default)
    {
        var budget = await budgetRepository.GetByOsIdAsync(osId, ct);
        if (budget is not null)
        {
            budget.Cancel($"OS cancelada: {reason}");
            await budgetRepository.UpdateAsync(budget, ct);
        }

        var payment = await paymentRepository.GetByOsIdAsync(osId, ct);
        if (payment is { Status: PaymentStatus.Pending })
        {
            payment.Fail($"OS cancelada: {reason}");
            await paymentRepository.UpdateAsync(payment, ct);
        }
    }
}

public class ExecutionCompletedHandler(
    IBudgetRepository budgetRepository,
    IPaymentRepository paymentRepository,
    IMercadoPagoService mercadoPagoService)
{
    /// <summary>
    /// Consumido quando ExecutionService conclui o serviço.
    /// Inicia checkout Mercado Pago e cria Payment Pending.
    /// Idempotente: se já existe Payment Pending ou Confirmed, não recria.
    /// </summary>
    public async Task HandleAsync(Guid osId, CancellationToken ct = default)
    {
        var budget = await budgetRepository.GetByOsIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"Orçamento para OS {osId} não encontrado ao processar ExecutionCompleted.");

        if (budget.Status != BudgetStatus.Approved)
            throw new InvalidOperationException($"Orçamento da OS {osId} não está Approved.");

        var existingPayment = await paymentRepository.GetByOsIdAsync(osId, ct);
        if (existingPayment is { Status: PaymentStatus.Pending or PaymentStatus.Confirmed })
            return;

        var amount = budget.CalculateTotal();
        var payment = Payment.Create(osId, budget.Id, amount);

        var checkoutUrl = await mercadoPagoService.CreatePreferenceAsync(
            payment.Id,
            osId,
            amount,
            $"Cobrança OS {osId} — {budget.VehiclePlate}",
            ct);

        payment.SetCheckoutUrl(checkoutUrl);
        await paymentRepository.AddAsync(payment, ct);
    }
}
