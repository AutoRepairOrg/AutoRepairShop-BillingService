using BillingService.Application.Events;
using BillingService.Application.Interfaces;
using BillingService.Application.Services;
using BillingService.Domain.Interfaces;
using BillingService.Infrastructure.MercadoPago;
using BillingService.Infrastructure.Messaging;
using BillingService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Database (PostgreSQL) ─────────────────────────────────────────────────────
builder.Services.AddDbContext<BillingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BillingDatabase")));

// ── Mensageria: RabbitMQ em produção, Stub em desenvolvimento ─────────────────
var useStubMessaging = builder.Configuration.GetValue<bool>("Messaging:UseStub");

if (useStubMessaging)
{
    builder.Services.AddScoped<IEventPublisher, StubEventPublisher>();
}
else
{
    builder.Services.AddSingleton<RabbitMQ.Client.IConnection>(_ =>
    {
        var factory = new RabbitMQ.Client.ConnectionFactory
        {
            HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost",
            Port     = int.Parse(builder.Configuration["RabbitMQ:Port"] ?? "5672"),
            UserName = builder.Configuration["RabbitMQ:User"] ?? "guest",
            Password = builder.Configuration["RabbitMQ:Password"] ?? "guest",
        };
        return factory.CreateConnectionAsync().GetAwaiter().GetResult();
    });
    builder.Services.AddScoped<IEventPublisher, RabbitMqEventPublisher>();
    builder.Services.AddHostedService<RabbitMqConsumerService>();
}

// ── Mercado Pago: real ou stub ────────────────────────────────────────────────
var useStubMp = builder.Configuration.GetValue<bool>("MercadoPago:UseStub");
if (useStubMp)
{
    builder.Services.AddSingleton<IMercadoPagoService, StubMercadoPagoService>();
}
else
{
    builder.Services.AddHttpClient<IMercadoPagoService, MercadoPagoService>();
}

// ── DI ────────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IBudgetRepository, BudgetRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<BudgetAppService>();
builder.Services.AddScoped<PaymentAppService>();
builder.Services.AddScoped<OsSentForApprovalHandler>();
builder.Services.AddScoped<OsCancelledHandler>();
builder.Services.AddScoped<ExecutionCompletedHandler>();

// ── API ───────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "BillingService API",
        Version = "v1",
        Description = "Microsserviço de Orçamentos & Pagamentos (Mercado Pago) — 12SOAT Fase 4"
    });
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<BillingDbContext>("postgresql");

var app = builder.Build();

// ── Migrations automáticas ────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
