using BillingService.Application.DTOs;
using BillingService.Application.Interfaces;
using BillingService.Domain.Entities.Payment;
using BillingService.Domain.Enums;
using BillingService.Domain.Interfaces;

namespace BillingService.Application.Services;

public class PaymentAppService(
    IPaymentRepository paymentRepository,
    IBudgetRepository budgetRepository,
    IMercadoPagoService mercadoPagoService,
    IEventPublisher eventPublisher)
{
    /// <summary>
    /// [Override manual] Inicia checkout Mercado Pago.
    /// No fluxo do Saga, o checkout é criado pelo ExecutionCompletedHandler.
    /// Proteção contra duplicatas: se já existe Payment Pending com URL, retorna o existente;
    /// se já está Confirmed, lança InvalidOperationException.
    /// </summary>
    public async Task<CheckoutResponse> StartCheckoutAsync(Guid osId, CancellationToken ct = default)
    {
        var existing = await paymentRepository.GetByOsIdAsync(osId, ct);
        if (existing is { Status: PaymentStatus.Pending, CheckoutUrl: not null })
            return new CheckoutResponse(existing.Id, existing.CheckoutUrl);

        if (existing is { Status: PaymentStatus.Confirmed })
            throw new InvalidOperationException($"Pagamento da OS {osId} já foi confirmado.");

        var budget = await budgetRepository.GetByOsIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"Orçamento para OS {osId} não encontrado.");

        if (budget.Status != BudgetStatus.Approved)
            throw new InvalidOperationException("Checkout só é permitido para orçamentos aprovados.");

        var amount = budget.CalculateTotal();
        if (amount <= 0)
            throw new InvalidOperationException("Orçamento sem valor para cobrança.");

        var payment = Payment.Create(osId, budget.Id, amount);
        var checkoutUrl = await mercadoPagoService.CreatePreferenceAsync(
            payment.Id,
            osId,
            amount,
            $"Orçamento OS {osId} — {budget.VehiclePlate}",
            ct);

        payment.SetCheckoutUrl(checkoutUrl);
        await paymentRepository.AddAsync(payment, ct);

        return new CheckoutResponse(payment.Id, checkoutUrl);
    }

    public async Task<PaymentResponse> GetByOsIdAsync(Guid osId, CancellationToken ct = default)
    {
        var payment = await paymentRepository.GetByOsIdAsync(osId, ct)
            ?? throw new KeyNotFoundException($"Pagamento para OS {osId} não encontrado.");

        return MapToResponse(payment);
    }

    /// <summary>
    /// Processa notificação do webhook Mercado Pago (topic=payment).
    /// Correlaciona via external_reference (PaymentId) retornado pela API do MP.
    /// </summary>
    public async Task ProcessWebhookAsync(string mpPaymentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mpPaymentId);

        var info = await mercadoPagoService.GetPaymentInfoAsync(mpPaymentId, ct);

        Payment? payment = await paymentRepository.GetByMercadoPagoIdAsync(mpPaymentId, ct);

        if (payment is null && Guid.TryParse(info.ExternalReference, out var paymentId))
            payment = await paymentRepository.GetByIdAsync(paymentId, ct);

        if (payment is null)
            throw new KeyNotFoundException(
                $"Pagamento com MercadoPagoId {mpPaymentId} não encontrado. " +
                "Certifique-se de que o checkout foi iniciado antes do webhook.");

        await ApplyStatusAsync(payment, info.Status, mpPaymentId, ct);
    }

    private async Task ApplyStatusAsync(Payment payment, MpPaymentStatus status, string mpPaymentId, CancellationToken ct)
    {
        if (payment.Status is not PaymentStatus.Pending)
            return;

        switch (status)
        {
            case MpPaymentStatus.Approved:
                payment.Confirm(mpPaymentId);
                break;
            case MpPaymentStatus.Rejected:
            case MpPaymentStatus.Cancelled:
                payment.Fail($"Mercado Pago: {status}");
                break;
            default:
                return;
        }

        await paymentRepository.UpdateAsync(payment, ct);
        await PublishDomainEventsAsync(payment, ct);
    }

    private async Task PublishDomainEventsAsync(Payment payment, CancellationToken ct)
    {
        foreach (var @event in payment.DomainEvents)
            await eventPublisher.PublishAsync(@event, ct);

        payment.ClearDomainEvents();
    }

    private static PaymentResponse MapToResponse(Payment payment) => new(
        payment.Id,
        payment.OsId,
        payment.BudgetId,
        payment.Amount,
        payment.Status.ToString(),
        payment.CheckoutUrl,
        payment.MercadoPagoPaymentId,
        payment.FailureReason,
        payment.CreatedAt,
        payment.UpdatedAt
    );
}
