using BillingService.Application.Events;
using BillingService.Application.Interfaces;
using BillingService.Application.Services;
using BillingService.Domain.Entities.Budget;
using BillingService.Domain.Entities.Payment;
using BillingService.Domain.Events;
using BillingService.Domain.Interfaces;
using FluentAssertions;
using Reqnroll;

namespace BillingService.Tests.BDD;

[Binding]
public class OrcamentoDaSagaSteps
{
    private readonly InMemoryBudgets _budgets = new();
    private readonly InMemoryPayments _payments = new();
    private readonly RecordingPublisher _publisher = new();
    private readonly OsSentForApprovalHandler _sentForApproval;
    private readonly OsCancelledHandler _cancelled;
    private readonly BudgetAppService _budgetsApp;
    private Guid _osId;
    private Guid _budgetId;

    public OrcamentoDaSagaSteps()
    {
        _sentForApproval = new OsSentForApprovalHandler(_budgets);
        _cancelled = new OsCancelledHandler(_budgets, _payments);
        _budgetsApp = new BudgetAppService(_budgets, _publisher);
    }

    [Given(@"a ordem ""(.*)"" enviada para aprovação com o item ""(.*)"" de (.*) para ""(.*)"" e o veículo ""(.*)""")]
    public async Task DadoOrdemEnviada(string osId, string item, decimal valor, string cliente, string placa)
    {
        _osId = Guid.Parse(osId);
        await _sentForApproval.HandleAsync(_osId, cliente, placa, [new IncomingOsItem(item, valor, 1)]);
        _budgetId = (await _budgetsApp.GetByOsIdAsync(_osId)).Id;
    }

    [When(@"o orçamento é enviado ao cliente")]
    public async Task QuandoEnviado()
    {
        await _budgetsApp.SendAsync(_budgetId);
    }

    [When(@"o cliente aprova o orçamento")]
    public async Task QuandoAprova()
    {
        await _budgetsApp.ApproveAsync(_budgetId);
    }

    [When(@"o cliente recusa o orçamento pelo motivo ""(.*)""")]
    public async Task QuandoRecusa(string motivo)
    {
        await _budgetsApp.RejectAsync(_budgetId, motivo);
    }

    [When(@"a ordem é cancelada pelo motivo ""(.*)""")]
    public async Task QuandoOrdemCancelada(string motivo)
    {
        await _cancelled.HandleAsync(_osId, motivo);
    }

    [Then(@"o orçamento está ""(.*)""")]
    public async Task EntaoStatus(string status)
    {
        var budget = await _budgetsApp.GetByOsIdAsync(_osId);
        budget.Status.Should().Be(status);
    }

    [Then(@"o evento ""(.*)"" foi publicado")]
    public void EntaoEvento(string nome)
    {
        _publisher.Events.Should().Contain(e => e.GetType().Name == nome);
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<DomainEvent> Events { get; } = [];

        public Task PublishAsync(DomainEvent @event, CancellationToken ct = default)
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryBudgets : IBudgetRepository
    {
        private readonly Dictionary<Guid, Budget> _items = [];

        public Task AddAsync(Budget budget, CancellationToken ct = default)
        {
            _items[budget.Id] = budget;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Budget budget, CancellationToken ct = default)
        {
            _items[budget.Id] = budget;
            return Task.CompletedTask;
        }

        public Task AddItemAsync(Budget budget, CancellationToken ct = default) => UpdateAsync(budget, ct);

        public Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_items.GetValueOrDefault(id));

        public Task<Budget?> GetByOsIdAsync(Guid osId, CancellationToken ct = default) =>
            Task.FromResult(_items.Values.FirstOrDefault(b => b.OsId == osId));
    }

    private sealed class InMemoryPayments : IPaymentRepository
    {
        public Task AddAsync(Payment payment, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Payment payment, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Payment?>(null);
        public Task<Payment?> GetByOsIdAsync(Guid osId, CancellationToken ct = default) => Task.FromResult<Payment?>(null);
        public Task<Payment?> GetByMercadoPagoIdAsync(string mpPaymentId, CancellationToken ct = default) => Task.FromResult<Payment?>(null);
    }
}
