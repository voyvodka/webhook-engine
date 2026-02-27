# AGENTS.md — WebhookEngine

## Project Overview

WebhookEngine is a self-hosted webhook delivery platform. Single ASP.NET Core host serves the REST API, background delivery workers, and a React dashboard (as static files from `wwwroot`). PostgreSQL is the only external dependency — used for data storage AND as a job queue via `SKIP LOCKED`.

## Tech Stack

- **Backend:** C# / .NET 10, ASP.NET Core (Controllers), Entity Framework Core, PostgreSQL 17+
- **Frontend (Dashboard):** React 19 + TypeScript 5.9 + Vite 7 + Tailwind CSS 4 + Recharts 3 + Lucide React (in `src/dashboard/`)
- **Testing:** xUnit, FluentAssertions, NSubstitute, Testcontainers (real PostgreSQL)
- **Logging:** Serilog (structured, JSON output)
- **Validation:** FluentValidation
- **Real-time:** SignalR
- **Observability:** OpenTelemetry + Prometheus metrics exporter
- **Deployment:** Docker Compose (2 containers: app + postgres)

## Solution Structure

```
WebhookEngine/
├── src/
│   ├── WebhookEngine.Core/           # Domain: entities, enums, interfaces, models, metrics, options
│   ├── WebhookEngine.Infrastructure/  # EF Core, PostgreSQL queue, repositories, services
│   ├── WebhookEngine.Application/     # DI registration (CQRS scaffold exists but not yet implemented)
│   ├── WebhookEngine.Worker/          # Background services (delivery, retry, circuit breaker, stale lock, retention)
│   ├── WebhookEngine.API/             # ASP.NET Core host, controllers, middleware, hubs, validators, auth
│   ├── WebhookEngine.Sdk/             # .NET SDK (NuGet)
│   └── dashboard/                     # React SPA (Vite + TypeScript + Tailwind CSS)
├── tests/
│   ├── WebhookEngine.Core.Tests/
│   ├── WebhookEngine.Infrastructure.Tests/
│   ├── WebhookEngine.Application.Tests/
│   ├── WebhookEngine.API.Tests/       # Integration tests
│   └── WebhookEngine.Worker.Tests/
├── docker/
│   ├── Dockerfile
│   ├── docker-compose.yml
│   └── docker-compose.dev.yml         # PostgreSQL only (for local dev)
├── .github/
│   └── workflows/
│       └── ci.yml                     # Build, test, Docker build
├── docs/                              # Architecture, API, Database, PRD docs
├── LICENSE                            # MIT
└── WebhookEngine.sln
```

## Build & Run Commands

```bash
# Restore & build entire solution
dotnet build WebhookEngine.sln

# Run the API host (includes worker + serves dashboard)
dotnet run --project src/WebhookEngine.API

# Run all tests
dotnet test WebhookEngine.sln

# Run a single test project
dotnet test tests/WebhookEngine.Core.Tests

# Run a single test by fully qualified name
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"

# Run tests matching a pattern
dotnet test --filter "DisplayName~circuit_breaker"

# Dashboard (React SPA) — uses Yarn, NOT npm
cd src/dashboard && yarn install
cd src/dashboard && yarn dev        # dev server
cd src/dashboard && yarn build      # production build → copies to API/wwwroot

# Docker
docker compose -f docker/docker-compose.yml up          # production (app + postgres)
docker compose -f docker/docker-compose.dev.yml up       # dev (PostgreSQL only)
```

## Code Style — C# Backend

### Naming Conventions
| Element              | Convention                    | Example                          |
|----------------------|-------------------------------|----------------------------------|
| Classes              | PascalCase                    | `HttpDeliveryService`            |
| Interfaces           | `I` + PascalCase              | `IMessageQueue`, `IDeliveryService` |
| Methods              | PascalCase + `Async` suffix   | `DeliverAsync`, `SignAsync`      |
| Parameters           | camelCase                     | `messageId`, `endpointUrl`       |
| Private fields       | `_` + camelCase               | `_httpClientFactory`             |
| Constants            | PascalCase                    | `MaxRetries`                     |
| Enums                | PascalCase (type + values)    | `MessageStatus.Delivered`        |
| Files                | Match class name              | `HmacSigningService.cs`          |

### Architecture Patterns
- **Controller-based:** Business logic currently lives in controllers (Application layer CQRS scaffold exists but is not yet implemented)
- **Repository pattern:** One repository per aggregate root in `Infrastructure/Repositories/`
- **Options pattern:** Configuration classes in `Core/Options/` (e.g., `RetryPolicyOptions`, `DeliveryOptions`, `CircuitBreakerOptions`, `RetentionOptions`, `DashboardAuthOptions`)
- **Dependency injection:** Constructor injection everywhere — no service locator
- **IHostedService:** All background workers (delivery, retry scheduler, circuit breaker, stale lock recovery, retention cleanup)
- **IHttpClientFactory:** For all outbound HTTP — never `new HttpClient()`
- **CancellationToken:** Pass through all async method chains

### Project Details

#### WebhookEngine.Core
```
Entities/          Application, DashboardUser, Endpoint, EndpointHealth, EventType, Message, MessageAttempt
Enums/             AttemptStatus, CircuitState, EndpointStatus, MessageStatus
Interfaces/        IDeliveryNotifier, IDeliveryService, IEndpointHealthTracker, IMessageQueue, ISigningService
Metrics/           WebhookMetrics (Prometheus counters/histograms)
Models/            DeliveryRequest, DeliveryResult, SignedHeaders
Options/           CircuitBreakerOptions, DashboardAuthOptions, DeliveryOptions, RetentionOptions, RetryPolicyOptions
```

#### WebhookEngine.Infrastructure
```
Data/              WebhookDbContext
Migrations/        EF Core migrations (auto-applied on startup)
Queue/             PostgresMessageQueue (SKIP LOCKED based queue)
Repositories/      ApplicationRepository, DashboardUserRepository, EndpointRepository, EventTypeRepository, MessageRepository
Services/          EndpointHealthTracker, HmacSigningService, HttpDeliveryService
```

#### WebhookEngine.Worker
```
DeliveryWorker.cs            # Polls queue, delivers webhooks
RetryScheduler.cs            # Schedules retries based on backoff policy
CircuitBreakerWorker.cs      # Monitors endpoint health, opens/closes circuits
StaleLockRecoveryWorker.cs   # Recovers messages stuck in 'sending' > 5 minutes
RetentionCleanupWorker.cs    # Daily cleanup of expired messages (03:00 UTC)
```

#### WebhookEngine.API
```
Auth/              PasswordHasher
Controllers/       ApplicationsController, AuthController, DashboardController, EndpointsController,
                   EventTypesController, HealthController, MessagesController
Hubs/              DeliveryHub + SignalRDeliveryNotifier (live delivery status via SignalR)
Middleware/        ApiKeyAuthMiddleware, ExceptionHandlingMiddleware, RequestLoggingMiddleware
Startup/           DashboardAdminSeeder (seeds first admin user from env vars)
Validators/        RequestValidators (FluentValidation rules)
wwwroot/           React dashboard build output
```

### Error Handling
- Catch `TaskCanceledException` for HTTP timeouts
- Catch `HttpRequestException` for connection failures
- Global `ExceptionHandlingMiddleware` returns structured error JSON
- Never throw from background workers — log and continue
- Return `DeliveryResult` with success/failure status, never throw on delivery failure

### Middleware Pipeline (order matters)
1. `RequestLoggingMiddleware`
2. `ExceptionHandlingMiddleware`
3. `ApiKeyAuthMiddleware` (for `/api/v1/*` routes)
4. Controllers / Static files

## Code Style — TypeScript Dashboard

### Naming Conventions
| Element          | Convention    | Example                    |
|------------------|---------------|----------------------------|
| Page components  | PascalCase    | `ApplicationsPage.tsx`     |
| UI components    | PascalCase    | `EndpointHealthBadge.tsx`  |
| Hooks            | camelCase     | `useDeliveryFeed.ts`       |
| Directories      | lowercase     | `pages/`, `components/`, `hooks/`, `api/` |

### Frontend Rules
- Use **Yarn** for all dependency management (never npm or pnpm)
- Vite for build tooling
- **Tailwind CSS v4** for styling (dark theme with custom tokens)
- **Lucide React** for icons
- **Recharts** for charts (delivery timeline)
- Build output goes to ASP.NET Core `wwwroot/` (via `vite.config.ts` outDir)
- Dashboard page load target: **< 2 seconds**
- ESLint + TypeScript strict mode for code quality

### Dashboard Structure
```
src/dashboard/src/
  api/               authApi.ts, dashboardApi.ts
  auth/              AuthContext.tsx (React context for session auth)
  components/        ConfirmModal, DeliveryTimeline, EndpointHealthBadge, EventTypeSelect,
                     Modal, PayloadViewer, RetryButton, Select
  hooks/             useDeliveryFeed.ts (SignalR live feed)
  layout/            AppShell.tsx (sidebar + main layout)
  pages/             ApplicationsPage, DashboardPage, DeliveryLogPage, EndpointsPage,
                     LoginPage, MessagesPage
  routes/            ProtectedRoute.tsx (auth guard)
  App.tsx, main.tsx, styles.css, types.ts
```

### Pages
| Page | Role |
|------|------|
| `LoginPage.tsx` | Cookie-based email/password authentication |
| `DashboardPage.tsx` | Overview — stat cards + delivery timeline chart |
| `ApplicationsPage.tsx` | App list with endpoint counts and health summary |
| `EndpointsPage.tsx` | Endpoint list with health badges (green/yellow/red) + create/edit/disable/delete |
| `MessagesPage.tsx` | Filterable message log (by event type, endpoint, status, date range) |
| `DeliveryLogPage.tsx` | Attempt detail — request headers, response body, status code, latency |

### Key Components
| Component | Role |
|-----------|------|
| `Modal.tsx` | Base modal component (centered, dark-themed) |
| `ConfirmModal.tsx` | Confirmation dialog (replaces browser-native confirm) |
| `Select.tsx` | Custom dropdown select (theme-consistent, replaces native select) |
| `EventTypeSelect.tsx` | Multi-select chip/toggle for event type filtering |
| `EndpointHealthBadge.tsx` | Color-coded health indicator (Active/Degraded/Failed) |
| `DeliveryTimeline.tsx` | Time-series chart (Recharts) — delivered vs failed buckets |
| `RetryButton.tsx` | Retry failed/dead-letter messages (calls `POST /messages/{id}/retry`) |
| `PayloadViewer.tsx` | JSON viewer with syntax highlighting |

### Dashboard Auth
- Cookie-based session auth (email/password), NOT API key
- First admin user seeded from env vars (`WebhookEngine__DashboardAuth__AdminEmail/Password`)
- Endpoints: `POST /api/v1/auth/login`, `POST /api/v1/auth/logout`, `GET /api/v1/auth/me`
- Post-MVP: OAuth (GitHub/Google)

### Dashboard API Endpoints
- `GET /api/v1/dashboard/overview` — last 24h stats, endpoint health summary, queue depth
- `GET /api/v1/dashboard/timeline?period=24h&interval=1h` — chart data (delivered/failed per bucket)

### Real-Time
- **SignalR** hub at `/hubs/deliveries` for live delivery status updates on the dashboard
- Messages transition (pending -> sending -> delivered/failed) pushed to connected clients
- `IDeliveryNotifier` interface + `SignalRDeliveryNotifier` implementation in API layer

## API Conventions

### URL Format
- Base: `/api/v1/`
- Resource names: kebab-case, plural (`/event-types`, `/endpoints`, `/messages`)
- Actions: `POST /messages/{id}/retry`, `POST /endpoints/{id}/disable`

### JSON
- Property names: camelCase (`eventType`, `idempotencyKey`, `createdAt`)
- Dates: ISO 8601 with timezone (`2026-02-26T14:30:00Z`)
- IDs: Prefixed strings (`whe_`, `evt_`, `ep_`, `msg_`, `att_`, `req_`)

### Response Envelope
```json
{ "data": { ... }, "meta": { "requestId": "req_..." } }                    // single
{ "data": [...], "meta": { "requestId": "...", "pagination": { ... } } }   // list
{ "error": { "code": "VALIDATION_ERROR", "message": "...", "details": [...] }, "meta": { ... } }
```

### HTTP Status Codes
- `200` success, `201` created, `202` accepted (async operations like message send)
- `400` validation, `401` unauthorized, `404` not found, `409` conflict/idempotency
- `422` unprocessable, `429` rate limited, `500` internal error

## Database Conventions

- **Tables:** snake_case, plural (`applications`, `event_types`, `messages`)
- **Columns:** snake_case (`api_key_hash`, `signing_secret`, `created_at`)
- **Indexes:** `idx_` prefix + table + columns (`idx_messages_queue`, `idx_endpoints_app_id`)
- **Foreign keys:** `ON DELETE CASCADE` for child records
- **Timestamps:** Always `TIMESTAMPTZ`, default `NOW()`
- **Primary keys:** `UUID DEFAULT gen_random_uuid()`
- **JSON columns:** Use `JSONB` (not `JSON`)
- **Soft deletes:** Use `is_archived` boolean, not actual deletion (for event types)
- EF Core migrations auto-applied on startup — **do NOT run `dotnet ef` commands manually**

## Observability

### Prometheus Metrics
- Exposed at `GET /metrics` (no auth required)
- Custom metrics defined in `Core/Metrics/WebhookMetrics.cs`
- Includes: `webhookengine_messages_enqueued`, `webhookengine_deliveries_total`, `webhookengine_deliveries_success`, `webhookengine_deliveries_failed`, `webhookengine_deadletter_total`, `webhookengine_delivery_duration` (histogram), `webhookengine_queue_depth`
- ASP.NET Core request metrics and .NET runtime metrics included automatically

### Structured Logging
- Serilog with JSON formatter
- Background workers log with correlation context: `MessageId`, `EndpointId`, `AttemptNumber`

## Backend Performance & Optimization Notes

### PostgreSQL Queue Tuning
- Queue polling uses a **partial index** (`idx_messages_queue` WHERE status = 'pending') — never remove or alter this index, it is critical for delivery throughput
- Delivery Worker dequeues in **batches of 10** (`LIMIT 10 FOR UPDATE SKIP LOCKED`) — this reduces round trips and lock contention
- Stale locks (worker crash) are recovered after **5 minutes** — messages stuck in `sending` with `locked_at` older than 5 min get reset to `pending`
- Single-instance throughput target: **100-500 deliveries/sec**; sustained >1000/sec requires migrating to Redis/RabbitMQ via `IMessageQueue` interface

### HTTP Client Rules
- Always use **`IHttpClientFactory`** — never instantiate `new HttpClient()` directly; this causes socket exhaustion and DNS caching issues
- Delivery timeout is configured via `DeliveryOptions` (default **30 seconds**) — set at the named client level (`"webhook-delivery"`)
- Catch `TaskCanceledException` for timeouts, `HttpRequestException` for connection failures — never let these propagate uncaught

### EF Core & Database Access
- Use **`AsNoTracking()`** on all read-only queries (list endpoints, message logs, dashboard stats) — avoids unnecessary change tracker overhead
- Guard against **N+1 queries** — use `.Include()` for related entities or project with `.Select()` to DTOs
- Raw SQL is acceptable for performance-critical paths (queue polling with `SKIP LOCKED`, dashboard aggregation queries)
- Never call `SaveChanges` inside a loop — batch operations into a single unit of work

### Memory & Resource Management
- Idle memory target: **< 256MB** for the entire host process
- Truncate `response_body` in `message_attempts` to **10KB max** — prevents storage explosion from large error pages
- Stream large payloads instead of buffering entirely in memory
- All background workers must respect **`CancellationToken`** — propagate it through every async call for graceful shutdown support

### Background Worker Rules
- **Never throw exceptions** from `IHostedService.ExecuteAsync` — catch, log (Serilog structured), and continue the loop
- Always check **circuit breaker state** before attempting delivery — skip endpoints with open circuits
- Workers should log with correlation context: `MessageId`, `EndpointId`, `AttemptNumber` for traceability
- On graceful shutdown (`CancellationToken` triggered), finish in-flight deliveries but stop dequeuing new messages

### Data Retention & Cleanup
- Delivered messages are purged after **30 days**, dead-letter after **90 days** (configurable)
- A daily cleanup background job (`RetentionCleanupWorker`) runs at **03:00 UTC** — deletes expired records in batches to avoid long-running transactions and table locks
- Without retention cleanup, the `messages` and `message_attempts` tables will grow unbounded and degrade query performance

## Important Architectural Decisions

1. **Single process:** API + Workers + Dashboard all in one ASP.NET Core host
2. **PostgreSQL as queue:** `SELECT ... FOR UPDATE SKIP LOCKED` — no Redis/RabbitMQ needed for MVP
3. **HMAC-SHA256 signing:** Follows [Standard Webhooks](https://www.standardwebhooks.com/) spec
4. **Circuit breaker:** Per-endpoint, 5 consecutive failures opens circuit, 5 min cooldown
5. **Retry policy:** 7 attempts with exponential backoff (5s, 30s, 2m, 15m, 1h, 6h, 24h)
6. **API key format:** `whe_{appIdShort}_{random32}` — stored as SHA256 hash, prefix for lookup
7. **At-least-once delivery:** Messages may be delivered more than once; never lost
8. **Bundled SPA:** React dashboard built into `wwwroot/`, served as static files — no separate frontend deployment
