using BillingService.Domain.Entities.Payment;
using BillingService.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BillingService.Infrastructure.Persistence;

public class PaymentRepository(BillingDbContext db) : IPaymentRepository
{
    public async Task AddAsync(Payment payment, CancellationToken ct = default)
    {
        await db.Payments.AddAsync(payment, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Payment payment, CancellationToken ct = default)
    {
        db.Payments.Update(payment);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Payment?> GetByOsIdAsync(Guid osId, CancellationToken ct = default)
        => await db.Payments
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(p => p.OsId == osId, ct);

    public async Task<Payment?> GetByMercadoPagoIdAsync(string mpPaymentId, CancellationToken ct = default)
        => await db.Payments.FirstOrDefaultAsync(p => p.MercadoPagoPaymentId == mpPaymentId, ct);
}
