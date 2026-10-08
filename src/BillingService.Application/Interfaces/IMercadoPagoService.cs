namespace BillingService.Application.Interfaces;

public enum MpPaymentStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
    Unknown
}

public record MpPaymentInfo(MpPaymentStatus Status, string? ExternalReference);

public interface IMercadoPagoService
{
    /// <summary>
    /// Cria Preference no Checkout Pro e retorna a URL de pagamento (init_point).
    /// external_reference = paymentId para correlacionar o webhook.
    /// </summary>
    Task<string> CreatePreferenceAsync(
        Guid paymentId,
        Guid osId,
        decimal amount,
        string description,
        CancellationToken ct = default);

    /// <summary>
    /// Consulta o status de um pagamento no Mercado Pago.
    /// </summary>
    Task<MpPaymentInfo> GetPaymentInfoAsync(string mpPaymentId, CancellationToken ct = default);
}
