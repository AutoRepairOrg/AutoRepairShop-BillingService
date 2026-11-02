namespace BillingService.Application.DTOs;

public record BudgetItemResponse(Guid Id, string Description, decimal Price, int Quantity, decimal Subtotal);

public record BudgetResponse(
    Guid Id,
    Guid OsId,
    string CustomerName,
    string VehiclePlate,
    string Status,
    decimal Total,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? RejectionReason,
    IEnumerable<BudgetItemResponse> Items);

public record RejectBudgetRequest(string Reason);
