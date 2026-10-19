using BillingService.Domain.Entities.Budget;
using BillingService.Domain.Enums;
using BillingService.Domain.Events;
using FluentAssertions;

namespace BillingService.Tests.Unit;

public class BudgetTests
{
    private static readonly Guid OsId = Guid.NewGuid();

    private static Budget CreateDraft() =>
        Budget.Create(OsId, "Maria Silva", "XYZ9876");

    [Fact]
    public void Create_WithValidData_ShouldStartAsDraft()
    {
        var budget = CreateDraft();

        budget.Status.Should().Be(BudgetStatus.Draft);
        budget.OsId.Should().Be(OsId);
        budget.VehiclePlate.Should().Be("XYZ9876");
        budget.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "XYZ9876")]
    [InlineData("Maria", "")]
    public void Create_WithMissingFields_ShouldThrow(string name, string plate)
    {
        var act = () => Budget.Create(OsId, name, plate);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithEmptyOsId_ShouldThrow()
    {
        var act = () => Budget.Create(Guid.Empty, "Maria", "XYZ9876");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddItem_ShouldCalculateTotal()
    {
        var budget = CreateDraft();
        budget.AddItem("Troca de óleo", 150m, 1);
        budget.AddItem("Filtro", 50m, 2);

        budget.CalculateTotal().Should().Be(250m);
    }

    [Fact]
    public void AddItem_WithNegativePrice_ShouldThrow()
    {
        var budget = CreateDraft();
        var act = () => budget.AddItem("Serviço", -10m, 1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Send_WithoutItems_ShouldThrow()
    {
        var budget = CreateDraft();
        var act = () => budget.Send();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Send_WithItems_ShouldPublishBudgetGeneratedEvent()
    {
        var budget = CreateDraft();
        budget.AddItem("Alinhamento", 200m);

        budget.Send();

        budget.Status.Should().Be(BudgetStatus.Sent);
        budget.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<BudgetGeneratedEvent>()
            .Which.Total.Should().Be(200m);
    }

    [Fact]
    public void Approve_FromSent_ShouldPublishBudgetApprovedEvent()
    {
        var budget = CreateDraft();
        budget.AddItem("Serviço", 100m);
        budget.Send();
        budget.ClearDomainEvents();

        budget.Approve();

        budget.Status.Should().Be(BudgetStatus.Approved);
        budget.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<BudgetApprovedEvent>();
    }

    [Fact]
    public void Reject_FromSent_ShouldPublishBudgetRejectedEvent()
    {
        var budget = CreateDraft();
        budget.AddItem("Serviço", 100m);
        budget.Send();
        budget.ClearDomainEvents();

        budget.Reject("Preço alto");

        budget.Status.Should().Be(BudgetStatus.Rejected);
        budget.RejectionReason.Should().Be("Preço alto");
        budget.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<BudgetRejectedEvent>();
    }

    [Fact]
    public void Approve_FromDraft_ShouldThrow()
    {
        var budget = CreateDraft();
        var act = () => budget.Approve();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddItem_AfterSend_ShouldThrow()
    {
        var budget = CreateDraft();
        budget.AddItem("Serviço", 100m);
        budget.Send();

        var act = () => budget.AddItem("Extra", 50m);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cancel_FromDraft_ShouldSetCancelledWithoutPublishingEvent()
    {
        var budget = CreateDraft();
        budget.AddItem("Serviço", 100m);

        budget.Cancel("OS cancelada pelo cliente");

        budget.Status.Should().Be(BudgetStatus.Cancelled);
        budget.RejectionReason.Should().Be("OS cancelada pelo cliente");
        budget.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Cancel_AlreadyCancelled_ShouldBeIdempotent()
    {
        var budget = CreateDraft();
        budget.Cancel("primeiro");
        budget.Cancel("segundo");

        budget.Status.Should().Be(BudgetStatus.Cancelled);
        budget.RejectionReason.Should().Be("primeiro");
    }

    [Fact]
    public void HappyPath_SendApprove_ShouldWork()
    {
        var budget = CreateDraft();
        budget.AddItem("Diagnóstico", 80m);
        budget.Send();
        budget.Approve();

        budget.Status.Should().Be(BudgetStatus.Approved);
        budget.CalculateTotal().Should().Be(80m);
    }
}
