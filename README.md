# AutoRepairShop — BillingService

Microsserviço responsável por **orçamentos e pagamentos** (integração [Mercado Pago](https://www.mercadopago.com.br/developers/pt)).  
Parte da arquitetura distribuída do **Tech Challenge Fase 4 · 12SOAT**.

## Arquitetura

```
src/
├── BillingService.Api            → Controllers REST, Swagger, Program.cs
├── BillingService.Application    → App Services, DTOs, Event Handlers (Saga consumers)
├── BillingService.Domain         → Entidades Budget/Payment, Enums, Domain Events
└── BillingService.Infrastructure → EF Core (PostgreSQL), RabbitMQ, Mercado Pago
tests/
└── BillingService.Tests
    └── Unit/                     → Testes unitários (xUnit + FluentAssertions + Moq)
k8s/                              → Deployment, Service, HPA, ConfigMap
.github/workflows/ci.yml          → CI/CD (Build → Testes → SonarQube → Docker → K8s)
```

## Banco de dados

**PostgreSQL 16** — banco próprio e isolado (ACID para transações financeiras).

## Fluxo de Status

### Orçamento (Budget)

```
Draft → Sent → Approved
              ↘ Rejected
```

### Pagamento (Payment)

```
Pending → Confirmed
        ↘ Failed
```

## Saga Pattern — Coreografado

### Eventos publicados (RabbitMQ · exchange: `billing-service-events`)

| Evento | Quando | Consumidor |
|---|---|---|
| `BudgetGeneratedEvent` | Orçamento enviado | ExecutionService |
| `BudgetApprovedEvent` | Cliente aprova | ExecutionService |
| `BudgetRejectedEvent` | Cliente rejeita | OsService (cancela OS) |
| `PaymentConfirmedEvent` | MP confirma pagamento | OsService (fecha OS) |
| `PaymentFailedEvent` | MP recusa pagamento | OsService |

### Eventos consumidos

| Evento | Origem | Ação |
|---|---|---|
| `OsCreated` | OsService | Cria Budget em Draft |
| `ExecutionCompleted` | ExecutionService | Inicia checkout MP + cria Payment |

## Integração Mercado Pago (Checkout Pro)

1. `POST /api/payment/checkout/{osId}` cria Preference e retorna `checkoutUrl`
2. Cliente paga na página do Mercado Pago
3. MP notifica `POST /api/payment/webhook`
4. Serviço consulta `/v1/payments/{id}` e confirma/falha o Payment
5. Publica `PaymentConfirmed` ou `PaymentFailed` no broker

Em desenvolvimento local, use `"MercadoPago:UseStub": true`.

## Endpoints REST

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/budget/{id}` | Busca orçamento por ID |
| `GET` | `/api/budget/os/{osId}` | Busca orçamento da OS |
| `POST` | `/api/budget/{id}/items` | Adiciona item (Draft) |
| `POST` | `/api/budget/{id}/send` | Envia orçamento (BudgetGenerated) |
| `POST` | `/api/budget/{id}/approve` | Aprova orçamento |
| `POST` | `/api/budget/{id}/reject` | Rejeita orçamento |
| `POST` | `/api/payment/checkout/{osId}` | Inicia checkout MP |
| `GET` | `/api/payment/os/{osId}` | Consulta pagamento |
| `POST` | `/api/payment/webhook` | Webhook Mercado Pago |
| `GET` | `/health` | Health check |

Swagger: `http://localhost:5011/swagger`

## Executando com Docker

```bash
docker compose up -d
```

Sobe API (porta **5011**), PostgreSQL (**5432**) e RabbitMQ (**5672** / UI **15672**).

## Testes

```bash
dotnet test
dotnet test --collect:"XPlat Code Coverage"
```

Cobertura mínima exigida: **80%**

## Tech Stack

- .NET 8 / ASP.NET Core
- Entity Framework Core + PostgreSQL 16
- Mercado Pago Checkout Pro (REST API)
- RabbitMQ (mensageria — Saga coreografado)
- xUnit + FluentAssertions + Moq
- Docker + Kubernetes
- GitHub Actions (CI/CD)
- SonarQube

## Autores

Projeto desenvolvido por **Dhiulia da Silva** e **Mateus Pinheiro** · 12SOAT Fase 4
