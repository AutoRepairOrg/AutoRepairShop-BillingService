namespace BillingService.Application.DTOs;

public record PaymentResponse(
    Guid Id,
    Guid OsId,
    Guid BudgetId,
    decimal Amount,
    string Status,
    string? CheckoutUrl,
    string? MercadoPagoPaymentId,
    string? FailureReason,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CheckoutResponse(Guid PaymentId, string CheckoutUrl);
