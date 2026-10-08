using BillingService.Application.DTOs;
using BillingService.Application.Interfaces;
using BillingService.Application.Services;
using BillingService.Domain.Entities.Budget;
using BillingService.Domain.Enums;
using BillingService.Domain.Events;
using BillingService.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace BillingService.Tests.Unit;

public class BudgetAppServiceTests
{
    private readonly Mock<IBudgetRepository> _repo = new();
    private readonly Mock<IEventPublisher> _publisher = new();
    private readonly BudgetAppService _sut;

    public BudgetAppServiceTests()
    {
        _sut = new BudgetAppService(_repo.Object, _publisher.Object);
    }

    private static Budget MakeSentBudget()
    {
        var budget = Budget.Create(Guid.NewGuid(), "João", "ABC1234");
        budget.AddItem("Serviço", 100m);
        budget.Send();
        budget.ClearDomainEvents();
        return budget;
    }

    [Fact]
    public async Task GetByIdAsync_Existing_ShouldReturnResponse()
    {
        var budget = MakeSentBudget();
        _repo.Setup(r => r.GetByIdAsync(budget.Id, default)).ReturnsAsync(budget);

        var result = await _sut.GetByIdAsync(budget.Id);

        result.Id.Should().Be(budget.Id);
        result.Status.Should().Be(BudgetStatus.Sent.ToString());
        result.Total.Should().Be(100m);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ShouldThrow()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Budget?)null);

        var act = async () => await _sut.GetByIdAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ApproveAsync_ShouldPersistAndPublish()
    {
        var budget = MakeSentBudget();
        _repo.Setup(r => r.GetByIdAsync(budget.Id, default)).ReturnsAsync(budget);
        _repo.Setup(r => r.UpdateAsync(budget, default)).Returns(Task.CompletedTask);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<DomainEvent>(), default)).Returns(Task.CompletedTask);

        var result = await _sut.ApproveAsync(budget.Id);

        result.Status.Should().Be(BudgetStatus.Approved.ToString());
        _repo.Verify(r => r.UpdateAsync(budget, default), Times.Once);
        _publisher.Verify(p => p.PublishAsync(It.IsAny<DomainEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task RejectAsync_ShouldPersistAndPublish()
    {
        var budget = MakeSentBudget();
        _repo.Setup(r => r.GetByIdAsync(budget.Id, default)).ReturnsAsync(budget);
        _repo.Setup(r => r.UpdateAsync(budget, default)).Returns(Task.CompletedTask);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<DomainEvent>(), default)).Returns(Task.CompletedTask);

        var result = await _sut.RejectAsync(budget.Id, "Caro demais");

        result.Status.Should().Be(BudgetStatus.Rejected.ToString());
        _publisher.Verify(p => p.PublishAsync(It.IsAny<DomainEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task SendAsync_ShouldPublishBudgetGenerated()
    {
        var budget = Budget.Create(Guid.NewGuid(), "Ana", "DEF5678");
        budget.AddItem("Pintura", 500m);

        _repo.Setup(r => r.GetByIdAsync(budget.Id, default)).ReturnsAsync(budget);
        _repo.Setup(r => r.UpdateAsync(budget, default)).Returns(Task.CompletedTask);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<DomainEvent>(), default)).Returns(Task.CompletedTask);

        var result = await _sut.SendAsync(budget.Id);

        result.Status.Should().Be(BudgetStatus.Sent.ToString());
        _publisher.Verify(p => p.PublishAsync(It.IsAny<DomainEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task AddItemAsync_ShouldUpdateBudget()
    {
        var budget = Budget.Create(Guid.NewGuid(), "Ana", "DEF5678");
        _repo.Setup(r => r.GetByIdAsync(budget.Id, default)).ReturnsAsync(budget);
        _repo.Setup(r => r.UpdateAsync(budget, default)).Returns(Task.CompletedTask);

        var result = await _sut.AddItemAsync(budget.Id, new AddBudgetItemRequest("Óleo", 80m, 1));

        result.Total.Should().Be(80m);
        result.Items.Should().ContainSingle();
    }
}
