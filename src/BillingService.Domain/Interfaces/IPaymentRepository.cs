using BillingService.Domain.Entities.Payment;

namespace BillingService.Domain.Interfaces;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken ct = default);
    Task UpdateAsync(Payment payment, CancellationToken ct = default);
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Payment?> GetByOsIdAsync(Guid osId, CancellationToken ct = default);
    Task<Payment?> GetByMercadoPagoIdAsync(string mpPaymentId, CancellationToken ct = default);
}
