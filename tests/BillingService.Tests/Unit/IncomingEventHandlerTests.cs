using BillingService.Application.Events;
using BillingService.Application.Interfaces;
using BillingService.Domain.Entities.Budget;
using BillingService.Domain.Entities.Payment;
using BillingService.Domain.Enums;
using BillingService.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace BillingService.Tests.Unit;

public class IncomingEventHandlerTests
{
    [Fact]
    public async Task OsCreatedHandler_ShouldCreateDraftBudget()
    {
        var repo = new Mock<IBudgetRepository>();
        var osId = Guid.NewGuid();

        repo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync((Budget?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<Budget>(), default)).Returns(Task.CompletedTask);

        var handler = new OsCreatedHandler(repo.Object);
        await handler.HandleAsync(osId, "Pedro", "BBB2222");

        repo.Verify(r => r.AddAsync(It.Is<Budget>(b =>
            b.OsId == osId &&
            b.Status == BudgetStatus.Draft &&
            b.CustomerName == "Pedro"), default), Times.Once);
    }

    [Fact]
    public async Task OsCreatedHandler_WhenBudgetExists_ShouldBeIdempotent()
    {
        var repo = new Mock<IBudgetRepository>();
        var osId = Guid.NewGuid();
        var existing = Budget.Create(osId, "Pedro", "BBB2222");

        repo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync(existing);

        var handler = new OsCreatedHandler(repo.Object);
        await handler.HandleAsync(osId, "Pedro", "BBB2222");

        repo.Verify(r => r.AddAsync(It.IsAny<Budget>(), default), Times.Never);
    }

    [Fact]
    public async Task ExecutionCompletedHandler_ShouldCreatePaymentWithCheckout()
    {
        var budgetRepo = new Mock<IBudgetRepository>();
        var paymentRepo = new Mock<IPaymentRepository>();
        var mp = new Mock<IMercadoPagoService>();

        var osId = Guid.NewGuid();
        var budget = Budget.Create(osId, "Cliente", "CCC3333");
        budget.AddItem("Serviço", 300m);
        budget.Send();
        budget.Approve();
        budget.ClearDomainEvents();

        budgetRepo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync(budget);
        paymentRepo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync((Payment?)null);
        mp.Setup(m => m.CreatePreferenceAsync(It.IsAny<Guid>(), osId, 300m, It.IsAny<string>(), default))
          .ReturnsAsync("https://mp.com/checkout");
        paymentRepo.Setup(r => r.AddAsync(It.IsAny<Payment>(), default)).Returns(Task.CompletedTask);

        var handler = new ExecutionCompletedHandler(budgetRepo.Object, paymentRepo.Object, mp.Object);
        await handler.HandleAsync(osId);

        paymentRepo.Verify(r => r.AddAsync(It.Is<Payment>(p =>
            p.OsId == osId &&
            p.Status == PaymentStatus.Pending &&
            p.CheckoutUrl == "https://mp.com/checkout"), default), Times.Once);
    }

    [Fact]
    public async Task ExecutionCompletedHandler_BudgetNotApproved_ShouldThrow()
    {
        var budgetRepo = new Mock<IBudgetRepository>();
        var paymentRepo = new Mock<IPaymentRepository>();
        var mp = new Mock<IMercadoPagoService>();

        var osId = Guid.NewGuid();
        var budget = Budget.Create(osId, "Cliente", "CCC3333");

        budgetRepo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync(budget);

        var handler = new ExecutionCompletedHandler(budgetRepo.Object, paymentRepo.Object, mp.Object);
        var act = async () => await handler.HandleAsync(osId);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
