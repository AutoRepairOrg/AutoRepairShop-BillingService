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

public class ExecutionCompletedHandler(
    IBudgetRepository budgetRepository,
    IPaymentRepository paymentRepository,
    IMercadoPagoService mercadoPagoService)
{
    /// <summary>
    /// Consumido quando ExecutionService conclui o serviço.
    /// Inicia checkout Mercado Pago e cria Payment Pending.
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
