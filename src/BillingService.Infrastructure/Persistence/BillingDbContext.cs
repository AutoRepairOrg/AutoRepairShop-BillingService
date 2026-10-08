using BillingService.Domain.Entities.Budget;
using BillingService.Domain.Entities.Payment;
using Microsoft.EntityFrameworkCore;

namespace BillingService.Infrastructure.Persistence;

/// <summary>
/// DbContext exclusivo do BillingService — PostgreSQL, sem acesso a outros serviços.
/// </summary>
public class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetItem> BudgetItems => Set<BudgetItem>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Budget>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CustomerName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.VehiclePlate).IsRequired().HasMaxLength(10);
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.HasIndex(e => e.OsId).IsUnique();

            entity.Ignore(e => e.Items);
            entity.Ignore(e => e.DomainEvents);

            entity.HasMany<BudgetItem>("_items")
                  .WithOne()
                  .HasForeignKey(i => i.BudgetId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BudgetItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Description).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Price).HasColumnType("numeric(18,2)");
            entity.Ignore(e => e.Subtotal);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Amount).HasColumnType("numeric(18,2)");
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.MercadoPagoPaymentId).HasMaxLength(100);
            entity.Property(e => e.CheckoutUrl).HasMaxLength(1000);
            entity.Property(e => e.FailureReason).HasMaxLength(500);
            entity.HasIndex(e => e.OsId);
            entity.HasIndex(e => e.MercadoPagoPaymentId);

            entity.Ignore(e => e.DomainEvents);
        });
    }
}
