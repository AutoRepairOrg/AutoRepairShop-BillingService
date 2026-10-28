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

    // ── OsSentForApprovalHandler ─────────────────────────────────────────────

    [Fact]
    public async Task OsSentForApprovalHandler_ShouldPopulateBudgetItems()
    {
        var repo = new Mock<IBudgetRepository>();
        var osId = Guid.NewGuid();
        var budget = Budget.Create(osId, "Pedro", "BBB2222");

        repo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync(budget);
        repo.Setup(r => r.AddItemAsync(budget, default)).Returns(Task.CompletedTask);

        var items = new List<IncomingOsItem>
        {
            new("Troca de óleo", 150m, 1),
            new("Filtro de ar", 80m, 2)
        };

        var handler = new OsSentForApprovalHandler(repo.Object);
        await handler.HandleAsync(osId, "Pedro", "BBB2222", items);

        budget.Items.Should().HaveCount(2);
        budget.CalculateTotal().Should().Be(310m);
        repo.Verify(r => r.AddItemAsync(It.IsAny<Budget>(), default), Times.Once);
    }

    [Fact]
    public async Task OsSentForApprovalHandler_WhenBudgetAlreadyHasItems_ShouldBeIdempotent()
    {
        var repo = new Mock<IBudgetRepository>();
        var osId = Guid.NewGuid();
        var budget = Budget.Create(osId, "Pedro", "BBB2222");
        budget.AddItem("Já existente", 100m);

        repo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync(budget);

        var handler = new OsSentForApprovalHandler(repo.Object);
        await handler.HandleAsync(osId, "Pedro", "BBB2222", [new IncomingOsItem("Novo", 50m, 1)]);

        budget.Items.Should().HaveCount(1);
        budget.Items.First().Description.Should().Be("Já existente");
        repo.Verify(r => r.AddItemAsync(It.IsAny<Budget>(), default), Times.Never);
    }

    [Fact]
    public async Task OsSentForApprovalHandler_WhenBudgetMissing_ShouldCreateBudgetWithItems()
    {
        var repo = new Mock<IBudgetRepository>();
        var osId = Guid.NewGuid();
        repo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync((Budget?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<Budget>(), default)).Returns(Task.CompletedTask);

        var handler = new OsSentForApprovalHandler(repo.Object);
        await handler.HandleAsync(osId, "Pedro", "BBB2222", [new IncomingOsItem("Troca de óleo", 150m, 1)]);

        repo.Verify(r => r.AddAsync(It.Is<Budget>(b =>
            b.OsId == osId &&
            b.CustomerName == "Pedro" &&
            b.VehiclePlate == "BBB2222" &&
            b.Items.Count == 1 &&
            b.CalculateTotal() == 150m), default), Times.Once);
    }

    [Fact]
    public async Task OsSentForApprovalHandler_WithoutItems_ShouldThrow()
    {
        var repo = new Mock<IBudgetRepository>();
        var handler = new OsSentForApprovalHandler(repo.Object);

        var act = async () => await handler.HandleAsync(Guid.NewGuid(), "Pedro", "BBB2222", []);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── OsCancelledHandler ───────────────────────────────────────────────────

    [Fact]
    public async Task OsCancelledHandler_ShouldCancelBudgetAndFailPendingPayment()
    {
        var budgetRepo = new Mock<IBudgetRepository>();
        var paymentRepo = new Mock<IPaymentRepository>();
        var osId = Guid.NewGuid();

        var budget = Budget.Create(osId, "Pedro", "BBB2222");
        budget.AddItem("Serviço", 200m);

        var payment = Payment.Create(osId, budget.Id, 200m);

        budgetRepo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync(budget);
        budgetRepo.Setup(r => r.UpdateAsync(budget, default)).Returns(Task.CompletedTask);
        paymentRepo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync(payment);
        paymentRepo.Setup(r => r.UpdateAsync(payment, default)).Returns(Task.CompletedTask);

        var handler = new OsCancelledHandler(budgetRepo.Object, paymentRepo.Object);
        await handler.HandleAsync(osId, "Cliente desistiu");

        budget.Status.Should().Be(BudgetStatus.Cancelled);
        payment.Status.Should().Be(PaymentStatus.Failed);
        budgetRepo.Verify(r => r.UpdateAsync(It.IsAny<Budget>(), default), Times.Once);
        paymentRepo.Verify(r => r.UpdateAsync(It.IsAny<Payment>(), default), Times.Once);
    }

    [Fact]
    public async Task OsCancelledHandler_NoBudgetOrPayment_ShouldNotThrow()
    {
        var budgetRepo = new Mock<IBudgetRepository>();
        var paymentRepo = new Mock<IPaymentRepository>();
        var osId = Guid.NewGuid();

        budgetRepo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync((Budget?)null);
        paymentRepo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync((Payment?)null);

        var handler = new OsCancelledHandler(budgetRepo.Object, paymentRepo.Object);
        var act = async () => await handler.HandleAsync(osId, "Motivo");

        await act.Should().NotThrowAsync();
    }

    // ── ExecutionCompletedHandler ────────────────────────────────────────────

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

    [Fact]
    public async Task ExecutionCompletedHandler_ExistingPendingPayment_ShouldBeIdempotent()
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

        var existing = Payment.Create(osId, budget.Id, 300m);
        existing.SetCheckoutUrl("https://mp.com/existing");

        budgetRepo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync(budget);
        paymentRepo.Setup(r => r.GetByOsIdAsync(osId, default)).ReturnsAsync(existing);

        var handler = new ExecutionCompletedHandler(budgetRepo.Object, paymentRepo.Object, mp.Object);
        await handler.HandleAsync(osId);

        paymentRepo.Verify(r => r.AddAsync(It.IsAny<Payment>(), default), Times.Never);
        mp.Verify(m => m.CreatePreferenceAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<string>(), default), Times.Never);
    }
}
