using BillingService.Domain.Entities.Payment;
using BillingService.Domain.Enums;
using BillingService.Domain.Events;
using FluentAssertions;

namespace BillingService.Tests.Unit;

public class PaymentTests
{
    private static readonly Guid OsId = Guid.NewGuid();
    private static readonly Guid BudgetId = Guid.NewGuid();

    private static Payment CreatePending() => Payment.Create(OsId, BudgetId, 350m);

    [Fact]
    public void Create_WithValidData_ShouldStartAsPending()
    {
        var payment = CreatePending();

        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.Amount.Should().Be(350m);
        payment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Create_WithZeroAmount_ShouldThrow()
    {
        var act = () => Payment.Create(OsId, BudgetId, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetCheckoutUrl_ShouldStoreUrl()
    {
        var payment = CreatePending();
        payment.SetCheckoutUrl("https://mp.com/checkout");
        payment.CheckoutUrl.Should().Be("https://mp.com/checkout");
    }

    [Fact]
    public void Confirm_ShouldPublishPaymentConfirmedEvent()
    {
        var payment = CreatePending();
        payment.Confirm("mp-123");

        payment.Status.Should().Be(PaymentStatus.Confirmed);
        payment.MercadoPagoPaymentId.Should().Be("mp-123");
        payment.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PaymentConfirmedEvent>()
            .Which.Amount.Should().Be(350m);
    }

    [Fact]
    public void Fail_ShouldPublishPaymentFailedEvent()
    {
        var payment = CreatePending();
        payment.Fail("Cartão recusado");

        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.FailureReason.Should().Be("Cartão recusado");
        payment.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PaymentFailedEvent>();
    }

    [Fact]
    public void Confirm_AlreadyConfirmed_ShouldThrow()
    {
        var payment = CreatePending();
        payment.Confirm("mp-1");

        var act = () => payment.Confirm("mp-2");
        act.Should().Throw<InvalidOperationException>();
    }
}
