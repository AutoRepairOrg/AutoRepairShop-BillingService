using BillingService.Application.Interfaces;
using BillingService.Application.Services;
using BillingService.Domain.Entities.Budget;
using BillingService.Domain.Entities.Payment;
using BillingService.Domain.Enums;
using BillingService.Domain.Events;
using BillingService.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace BillingService.Tests.Unit;

public class PaymentAppServiceTests
{
    private readonly Mock<IPaymentRepository> _paymentRepo = new();
    private readonly Mock<IBudgetRepository> _budgetRepo = new();
    private readonly Mock<IMercadoPagoService> _mp = new();
    private readonly Mock<IEventPublisher> _publisher = new();
    private readonly PaymentAppService _sut;

    public PaymentAppServiceTests()
    {
        _sut = new PaymentAppService(
            _paymentRepo.Object,
            _budgetRepo.Object,
            _mp.Object,
            _publisher.Object);
    }

    private static Budget MakeApprovedBudget(Guid osId)
    {
        var budget = Budget.Create(osId, "Cliente", "AAA1111");
        budget.AddItem("Reparo", 400m);
        budget.Send();
        budget.Approve();
        budget.ClearDomainEvents();
        return budget;
    }

    [Fact]
    public async Task StartCheckoutAsync_ShouldCreatePaymentAndReturnUrl()
    {
        var osId = Guid.NewGuid();
        var budget = MakeApprovedBudget(osId);

        _paymentRepo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync((Payment?)null);
        _budgetRepo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync(budget);
        _mp.Setup(m => m.CreatePreferenceAsync(It.IsAny<Guid>(), osId, 400m, It.IsAny<string>(), default))
           .ReturnsAsync("https://mp.com/pay");
        _paymentRepo.Setup(r => r.AddAsync(It.IsAny<Payment>(), default)).Returns(Task.CompletedTask);

        var result = await _sut.StartCheckoutAsync(osId);

        result.CheckoutUrl.Should().Be("https://mp.com/pay");
        result.PaymentId.Should().NotBeEmpty();
        _paymentRepo.Verify(r => r.AddAsync(It.IsAny<Payment>(), default), Times.Once);
    }

    [Fact]
    public async Task StartCheckoutAsync_BudgetNotApproved_ShouldThrow()
    {
        var osId = Guid.NewGuid();
        var budget = Budget.Create(osId, "Cliente", "AAA1111");
        budget.AddItem("Item", 10m);

        _paymentRepo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync((Payment?)null);
        _budgetRepo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync(budget);

        var act = async () => await _sut.StartCheckoutAsync(osId);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ProcessWebhookAsync_Approved_ShouldConfirmAndPublish()
    {
        var payment = Payment.Create(Guid.NewGuid(), Guid.NewGuid(), 200m);
        payment.SetCheckoutUrl("https://mp.com");

        _mp.Setup(m => m.GetPaymentInfoAsync("mp-99", default))
           .ReturnsAsync(new MpPaymentInfo(MpPaymentStatus.Approved, payment.Id.ToString()));
        _paymentRepo.Setup(r => r.GetByMercadoPagoIdAsync("mp-99", default)).ReturnsAsync((Payment?)null);
        _paymentRepo.Setup(r => r.GetByIdAsync(payment.Id, default)).ReturnsAsync(payment);
        _paymentRepo.Setup(r => r.UpdateAsync(payment, default)).Returns(Task.CompletedTask);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<DomainEvent>(), default)).Returns(Task.CompletedTask);

        await _sut.ProcessWebhookAsync("mp-99");

        payment.Status.Should().Be(PaymentStatus.Confirmed);
        _publisher.Verify(p => p.PublishAsync(It.IsAny<DomainEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookAsync_Rejected_ShouldFailAndPublish()
    {
        var payment = Payment.Create(Guid.NewGuid(), Guid.NewGuid(), 200m);

        _mp.Setup(m => m.GetPaymentInfoAsync("mp-fail", default))
           .ReturnsAsync(new MpPaymentInfo(MpPaymentStatus.Rejected, payment.Id.ToString()));
        _paymentRepo.Setup(r => r.GetByMercadoPagoIdAsync("mp-fail", default)).ReturnsAsync((Payment?)null);
        _paymentRepo.Setup(r => r.GetByIdAsync(payment.Id, default)).ReturnsAsync(payment);
        _paymentRepo.Setup(r => r.UpdateAsync(payment, default)).Returns(Task.CompletedTask);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<DomainEvent>(), default)).Returns(Task.CompletedTask);

        await _sut.ProcessWebhookAsync("mp-fail");

        payment.Status.Should().Be(PaymentStatus.Failed);
        _publisher.Verify(p => p.PublishAsync(It.IsAny<DomainEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task GetByOsIdAsync_NotFound_ShouldThrow()
    {
        _paymentRepo.Setup(r => r.GetByOsIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Payment?)null);

        var act = async () => await _sut.GetByOsIdAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
