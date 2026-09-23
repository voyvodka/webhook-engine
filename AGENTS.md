# AGENTS.md — WebhookEngine

Instructions for coding agents working in this repository. `CLAUDE.md` imports this file and adds
only Claude Code–specific notes, so project rules belong here.

## Project

**WebhookEngine** — Self-hosted webhook delivery platform.

Queue-based webhook delivery engine with retry logic, circuit breaker, HMAC signing, and a React dashboard for monitoring. A single ASP.NET Core host serves the REST API, background delivery workers, and the React dashboard (as static files from `wwwroot`). PostgreSQL is the only external dependency — used for data storage AND as a job queue via `SKIP LOCKED`. SignalR carries real-time updates. Self-hosted via Docker Compose.

**Core Value:** Reliable, observable webhook delivery — messages must reach their endpoints with guaranteed retry, proper signing, and full delivery visibility.

### Tech Stack

Exact versions live in the `.csproj` files and `src/dashboard/package.json`.

- **Backend:** C# / .NET 10, ASP.NET Core (Controllers), Entity Framework Core 10, PostgreSQL 17+
- **Frontend (Dashboard):** React 19 + TypeScript 6 + Vite 8 + Tailwind CSS 4 + TanStack Query 5 + React Router 8 + Recharts 3 + Lucide React (in `src/dashboard/`)
- **Testing:** xUnit, FluentAssertions, NSubstitute, Testcontainers (real PostgreSQL)
- **Logging:** Serilog (structured, JSON output)
- **Validation:** FluentValidation
- **Real-time:** SignalR
- **Observability:** OpenTelemetry + Prometheus metrics exporter
- **Deployment:** Docker Compose (2 containers: app + postgres)

### Constraints

- **Stack lock:** .NET 10, React 19, PostgreSQL — no stack changes.
- **API:** No breaking changes; the `v1` prefix is preserved.
- **Package manager:** **Bun** for the frontend (never npm, yarn, or pnpm). NuGet for the backend. Never mix package managers.
- **PostgreSQL-only:** Redis/RabbitMQ/Kafka are out of scope. Queueing uses `SKIP LOCKED`; distributed locking uses advisory locks.
- **Single-process host:** API + Workers + Dashboard all run inside the same `WebApplication`.
- **Standard Webhooks spec:** Signature header names and format are fixed — no breaking changes.
- **Migrations:** EF Core migrations are applied on startup. Never generate migrations or run `dotnet ef` commands without explicit user consent.

### Documentation language rule

All Markdown files committed to git (root `*.md`, `docs/**`, `samples/**`, etc.) **must be in English**. Internal notes under `.planning/` are gitignored and may stay in Turkish. `AGENTS.md` and `CLAUDE.md` are committed → English.

---

## Working Notes — `.planning/`

Active task tracking and personal notes live in `.planning/` (gitignored, not public). Read these in order at the start of a new session to build context:

| File | Purpose |
|---|---|
| `.planning/ROADMAP.md` | Where the project is and where it's headed |
| `.planning/TODO.md` | Active and upcoming tasks |
| `.planning/NOTES.md` | Decision archive, design sketches, known tech debt |
| `.planning/README.md` | How this folder is used |

**Keeping it fresh:**
- When a task completes, **immediately** strike it through or remove it from `TODO.md`.
- When a new decision or learning emerges, drop a short note in `NOTES.md`.
- At phase transitions, update `.planning/ROADMAP.md` and sync the public `docs/ROADMAP.md`.

**Public documentation** lives in `docs/` (ROADMAP, PRD, ARCHITECTURE, API, DATABASE, GETTING-STARTED, SELF-HOSTING, RELEASE) and `docs/adr/` (architectural decisions). External-facing changes go there; `.planning/` is for day-to-day work.

---

## Solution Structure

```
WebhookEngine/
├── src/
│   ├── WebhookEngine.Core/            # Entities, Enums, Interfaces, Options, Metrics, StateMachine — 0 NuGet deps
│   ├── WebhookEngine.Infrastructure/  # EF Core, PostgreSQL queue, repositories, services
│   ├── WebhookEngine.Worker/          # 6 BackgroundServices
│   ├── WebhookEngine.API/             # Controllers, middleware, SignalR, validators, auth, dashboard SPA host
│   ├── WebhookEngine.Sdk/             # .NET client (NuGet)
│   └── dashboard/                     # React 19 SPA
├── packages/endpoint-manager/         # @webhookengine/endpoint-manager (embeddable portal UI)
├── samples/                           # Sender, receiver, signature verification, portal host
├── tests/
│   ├── WebhookEngine.Core.Tests/
│   ├── WebhookEngine.Infrastructure.Tests/
│   ├── WebhookEngine.API.Tests/       # Integration tests
│   ├── WebhookEngine.Worker.Tests/
│   ├── WebhookEngine.Sdk.Tests/
│   └── benchmark/
├── docker/
│   ├── Dockerfile
│   ├── docker-compose.yml
│   └── docker-compose.dev.yml         # PostgreSQL only (for local dev)
├── scripts/                           # Release smoke test, GitHub label/milestone setup
├── .github/workflows/                 # ci, codeql, dependency-review, release, pages, publish-portal, sync-bun-lock
├── docs/                              # Architecture, API, Database, PRD docs + ADRs
├── LICENSE                            # MIT
└── WebhookEngine.sln
```

**Workers:** `DeliveryWorker` (queue + delivery), `RetryScheduler` (Failed → Pending), `CircuitBreakerWorker` (Open → HalfOpen), `StaleLockRecoveryWorker` (release locks after worker crashes), `RetentionCleanupWorker` (purge old messages), `QueueMetricsWorker` (queue-depth metrics).

**Flow:** API → enqueue as `Pending` → `DeliveryWorker` dequeues with `FOR UPDATE SKIP LOCKED` → HMAC sign → HTTP POST → success: `Delivered`, fail+retry: `Failed`, max retries exhausted: `DeadLetter`. State transitions are guarded by `MessageStateMachine`.

Details: `docs/ARCHITECTURE.md`, `docs/DATABASE.md`, `docs/API.md`.

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

# Dashboard (React SPA) — uses Bun, NOT npm/yarn/pnpm
cd src/dashboard && bun install
cd src/dashboard && bun run dev     # dev server
cd src/dashboard && bun run build   # production build → copies to API/wwwroot

# Docker
docker compose -f docker/docker-compose.yml up          # production (app + postgres)
docker compose -f docker/docker-compose.dev.yml up       # dev (PostgreSQL only)
```

---

## Conventions

Detailed per-area rules live in `.claude/rules/` — read the relevant file before writing code in that area.

| File | Covers |
|---|---|
| `.claude/rules/core-domain.md` | Entity / Enum / Interface / Options conventions |
| `.claude/rules/infrastructure.md` | EF Core, repositories, queue, advisory locks, migrations |
| `.claude/rules/backend-api.md` | Controllers, middleware, validators, SignalR, ApiEnvelope |
| `.claude/rules/workers.md` | `BackgroundService` pattern (scope-per-iteration, error backoff) |
| `.claude/rules/dashboard.md` | React 19 + Vite 8 + Tailwind 4 + TanStack Query + Bun conventions, SignalR client, lazy routes |
| `.claude/rules/sdk.md` | `WebhookEngine.Sdk` public surface, `WebhookVerifier`, NuGet metadata |
| `.claude/rules/testing.md` | xUnit + Testcontainers (no mocked DB), race-condition tests, naming |

**Highlights:**
- C# 4-space indent; async methods end with `Async`; `CancellationToken ct = default` is the last parameter; PascalCase namespaces.
- React: PascalCase components, camelCase hooks / utilities, named exports (default export only for lazy-loaded pages), strict TypeScript.
- All API responses use `ApiEnvelope`; validation goes through FluentValidation.
- Read queries always use `.AsNoTracking()`.
- Status transitions on `Message` go through `MessageRepository.Mark*Async` CAS guards (`WHERE LockedBy = @lockedBy AND Status = Sending`); callers must check the `bool` result.
- `EndpointHealth` mutations go through `EndpointHealthTracker.WithEndpointLockAsync` (advisory-lock namespace `100_001`).
- HTTP delivery uses the named `webhook-delivery` client only — never `new HttpClient()`. `SocketsHttpHandler.ConnectCallback` pins resolved IPs (DNS-rebinding defense).
- `WebhookMetrics? metrics = null` is the optional-dependency pattern.
- Tests **never mock the database** (project rule from a past mock-vs-prod incident) — use Testcontainers.

**Comments — sparing, not generous.** Default to none. Add one only when the **why** is non-obvious: a load-bearing ordering, a hidden invariant, a workaround for a known bug. Never restate *what* the code does (names handle that), never reference the current PR / task / caller (rots fast), never expand a one-line reason into a paragraph. One short line beats five. If deleting the comment wouldn't confuse a future reader, it shouldn't have been there. Same rule for commit messages, YAML, JSON, and `# this does X` headers — keep prose for things the code itself can't say.

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
- **Controller-based:** Business logic lives in controllers (Application layer fully removed — see ADR-002)
- **Repository pattern:** One repository per aggregate root in `Infrastructure/Repositories/`
- **Options pattern:** Configuration classes in `Core/Options/` (e.g., `RetryPolicyOptions`, `DeliveryOptions`, `CircuitBreakerOptions`, `RetentionOptions`, `DashboardAuthOptions`)
- **Dependency injection:** Constructor injection everywhere — no service locator
- **IHostedService:** All background workers (delivery, retry scheduler, circuit breaker, stale lock recovery, retention cleanup, queue metrics)
- **IHttpClientFactory:** For all outbound HTTP — never `new HttpClient()`
- **CancellationToken:** Pass through all async method chains

### Project Details

#### WebhookEngine.Core
```
Entities/          Application, AuditLog, DashboardUser, Endpoint, EndpointHealth, EventType, Message, MessageAttempt
Enums/             AttemptStatus, CircuitState, EndpointStatus, MessageStatus, PortalCapability
Interfaces/        IApplicationRateLimiter, IAuditLogger, IDeliveryNotifier, IDeliveryService, IEndpointHealthTracker,
                   IEndpointRateLimiter, IEndpointTester, IMessageQueue, IMessageStateMachine, IPayloadTransformer, ISigningService
StateMachine/      MessageStateMachine
Utilities/         LogSanitizer, RateLimitResolver
Metrics/           WebhookMetrics (Prometheus counters/histograms)
Models/            DeliveryRequest, DeliveryResult, EndpointTestModels, SignedHeaders
Options/           CircuitBreakerOptions, DashboardAuthOptions, DeliveryOptions, LoginRateLimitOptions, PortalAuthOptions,
                   RateLimitOptions, RetentionOptions, RetryPolicyOptions, SsrfGuardOptions, TransformationOptions
```

#### WebhookEngine.Infrastructure
```
Data/              WebhookDbContext
Migrations/        EF Core migrations (auto-applied on startup)
Queue/             PostgresMessageQueue (SKIP LOCKED based queue)
Repositories/      ApplicationRepository, AuditLogRepository, DashboardStatsRepository, DashboardUserRepository,
                   EndpointRepository, EventTypeRepository, MessageRepository
Services/          ApplicationRateLimiter, AuditLogger, DeliveryHttpRequestOptions, DeliveryLookupCache, EndpointHealthTracker,
                   EndpointRateLimiter, EndpointTester, HmacSigningService, HttpDeliveryService, IpAllowlistMatcher,
                   JmesPathPayloadTransformer, PortalLookupCache, PrivateIpDetector
```

#### WebhookEngine.Worker
```
DeliveryWorker.cs            # Polls queue, delivers webhooks
RetryScheduler.cs            # Schedules retries based on backoff policy
CircuitBreakerWorker.cs      # Monitors endpoint health, opens/closes circuits
StaleLockRecoveryWorker.cs   # Recovers messages stuck in 'sending' > 5 minutes
RetentionCleanupWorker.cs    # Daily cleanup of expired messages (03:00 UTC)
QueueMetricsWorker.cs        # Publishes queue-depth metrics
```

#### WebhookEngine.API
```
Audit/             AuditContextExtensions
Auth/              PasswordHasher
Contracts/         ApiEnvelope, ApiResponseDtos, PaginationBounds, TimestampBounds, Portal/
Controllers/       ApplicationsController, AuditLogsController, AuthController, DashboardAnalyticsController,
                   DashboardEndpointController, DashboardMessagesController, DashboardPortalController,
                   DevTrafficController, EndpointsController, EventTypesController, HealthController,
                   MessagesController, PortalEndpointsController
Hubs/              DeliveryHub + SignalRDeliveryNotifier (live delivery status via SignalR)
Middleware/        ApiKeyAuthMiddleware, ExceptionHandlingMiddleware, MetricsAuthMiddleware, PortalCorsMiddleware,
                   PortalTokenAuthMiddleware, RequestLoggingMiddleware, SecurityHeadersMiddleware
Services/          AppReadinessGate, DevTrafficGenerator, EndpointTrafficProfiler, TrafficScheduler
Startup/           DashboardAdminSeeder (seeds first admin user from env vars), options validators
Validators/        RequestValidators, PortalRequestValidators, EndpointValidationRules, URL/header policies (FluentValidation)
wwwroot/           React dashboard build output
```

### Error Handling
- Catch `TaskCanceledException` for HTTP timeouts
- Catch `HttpRequestException` for connection failures
- Global `ExceptionHandlingMiddleware` returns structured error JSON
- Never throw from background workers — log and continue
- Return `DeliveryResult` with success/failure status, never throw on delivery failure

### Middleware Pipeline (order matters — see `Program.cs`)
1. `SecurityHeadersMiddleware`
2. `MetricsAuthMiddleware`
3. `RequestLoggingMiddleware`
4. `ExceptionHandlingMiddleware`
5. `ApiKeyAuthMiddleware` (for `/api/v1/*` routes)
6. `PortalTokenAuthMiddleware`, `PortalCorsMiddleware`
7. Rate limiter, authentication, authorization
8. Controllers / Static files

## Code Style — TypeScript Dashboard

### Naming Conventions
| Element          | Convention    | Example                    |
|------------------|---------------|----------------------------|
| Page components  | PascalCase    | `ApplicationsPage.tsx`     |
| UI components    | PascalCase    | `EndpointHealthBadge.tsx`  |
| Hooks            | camelCase     | `useDeliveryFeed.ts`       |
| Directories      | lowercase     | `pages/`, `components/`, `hooks/`, `api/` |

### Frontend Rules
- Use **Bun** for all dependency management (never npm, yarn, or pnpm)
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
  components/        ConfirmModal, DeliveryTimeline, EndpointHealthBadge, EndpointTestModal, EventTypeSelect,
                     Logo, Modal, PayloadViewer, PortalAccessModal, RetryButton, RouteErrorBoundary, Select,
                     StatusBadge, TransformSection
  hooks/             useDeliveryFeed.ts (SignalR live feed)
  layout/            AppShell.tsx (sidebar + main layout)
  pages/             ApplicationsPage, DashboardPage, DeliveryLogPage, EndpointsPage, EventTypesPage,
                     LoginPage, MessagesPage
  routes/            ProtectedRoute.tsx (auth guard)
  utils/             dateTime.ts, editorTheme.ts, styles.ts
  App.tsx, main.tsx, styles.css, types.ts
```

### Pages
| Page | Role |
|------|------|
| `LoginPage.tsx` | Cookie-based email/password authentication |
| `DashboardPage.tsx` | Overview — stat cards + delivery timeline chart |
| `ApplicationsPage.tsx` | App list with endpoint counts and health summary |
| `EndpointsPage.tsx` | Endpoint list with health badges (green/yellow/red) + create/edit/disable/delete |
| `EventTypesPage.tsx` | Event type management |
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
- Exposed at `GET /metrics` — public by default; when `WebhookEngine:Metrics:ScrapeToken` is set, requires `Authorization: Bearer <token>`
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

---

## GitHub Workflow (Issue, Commit, Push, PR)

### Issue Triage Rules
- New issues should start with `status: needs-triage`.
- After validation, move to `status: triaged` and add exactly one priority label: `priority: p0` or `priority: p1` or `priority: p2`.
- Add component labels (`api`, `dashboard`, `worker`, `sdk`, `infrastructure`, `database`, `security`, `performance`) and set a milestone.
- Use `regression` label when behavior was working in an earlier release.
- When an issue is marked `status: needs-triage`, first triage (repro, priority, component, milestone) before implementation.

### Commit Message and Issue Linking
- Conventional commit messages (`feat:`, `fix:`, `docs:`, `refactor:`, `chore:`, …).
- While work is in progress, reference issues with `Refs #<issue-number>`.
- When the change is complete and should close the issue, put `Closes #<issue-number>` in the PR body (or the final commit that reaches `main`).
- Do not close an issue before the fix is pushed and visible on remote.
- If a fix is merged but should close only after a patch release, use `Refs #<issue-number>` and close manually after the tag is published.
- Keep one issue per fix when possible; avoid bundling unrelated issue closures in one commit.

### PR Workflow

`main` is protected — direct push is reserved for trivial admin overrides. Anything else flows through a feature branch + PR + green CI + squash-merge. The repo deletes head branches automatically on merge.

#### Branch naming
- `feature/<short-slug>` — new functionality (`feature/dashboard-logo`)
- `fix/<short-slug>` — bug or security fix (`fix/codeql-log-forging-and-pii`)
- `refactor/<short-slug>` — internal restructuring with no behavior change
- `docs/<short-slug>` — docs-only or repo-meta updates (`docs/pr-label-policy`)
- `chore/<short-slug>` — config / tooling tweaks
- `dependabot/...` — created automatically; do not rename

#### Post-merge cleanup (remote + local)

The repo has `delete_branch_on_merge=true` enabled, so merged head branches disappear from `origin` automatically. To stay in sync locally, the repo's `.git/config` carries `fetch.prune=true`, which means **every** `git fetch` / `git pull` removes stale `origin/...` tracking refs in one step. After a PR merges, run:

```bash
git checkout main && git pull --ff-only origin main
git for-each-ref --format='%(refname:short) %(upstream:track)' refs/heads \
  | awk '$2=="[gone]" && $1!="main" {print $1}' \
  | xargs -r git branch -D
```

The `awk` line drops every local branch whose upstream has gone away (the merged feature branch). Runs as a no-op when there is nothing to clean.

#### Required labels per PR
Every PR carries **at least one type label** and **at least one area label** so the changelog can be grouped at release time. Apply via `gh pr edit <n> --add-label <label>` or the PR sidebar.

**Type (pick one):**
| Label | When |
|---|---|
| `enhancement` | New user-visible feature or improvement |
| `bug` | Defect fix that restores intended behavior |
| `security` | Resolves a CodeQL/secret-scanning/CVE alert or hardens a vulnerability |
| `performance` | Measurable latency / throughput / memory improvement |
| `regression` | Reverts or repairs a behavior previously working |
| `documentation` | Public docs (`docs/`, `README`, `CHANGELOG`, ADRs) or `AGENTS.md` / `CLAUDE.md` |
| `dependencies` | Lib/SDK/runtime version bump (Dependabot adds this automatically) |

**Area (pick all that apply):**
| Label | Touches |
|---|---|
| `api` | `src/WebhookEngine.API/` controllers, middleware, DTOs, validators |
| `worker` | `src/WebhookEngine.Worker/` background services |
| `infrastructure` | `src/WebhookEngine.Infrastructure/` repos, queue, services, migrations |
| `database` | EF migrations, schema, indexes, raw SQL |
| `dashboard` | `src/dashboard/` React SPA |
| `sdk` | `src/WebhookEngine.Sdk/` and `samples/signature-verification/` |
| `ci` | `.github/workflows/`, `.github/dependabot.yml`, build/release tooling |
| `docker` | `docker/Dockerfile`, compose files, base-image bumps |
| `nuget` | NuGet package bumps (Dependabot ecosystem label) |
| `npm` | npm/Bun package bumps (Dependabot ecosystem label) |

**Priority and triage labels** (`priority: p0/p1/p2`, `status: needs-triage/triaged/blocked`) are applied to issues, not normally to PRs. Use `good first issue` / `help wanted` only on issues open for contribution.

#### Release-note grouping
The Unreleased section of `CHANGELOG.md` mirrors the type labels (`### Added` / `### Changed` / `### Fixed` / `### Removed` / `### Security`). When merging, append the PR's user-facing summary under the section matching its type label.

### Open Work Analysis Routine
- At the start of a development cycle (or when requested), analyze open items before execution:
  - open issues (status/priority/component/milestone),
  - open PRs and review/check states,
  - recent workflow failures and pending release actions.
- Publish a concise **Required Next Actions** list ordered by priority (P0 -> P2), with clear owner/action/output.

---

## Release & Versioning

- **SemVer:** `v{major}.{minor}.{patch}`; tags are prefixed with `v`.
- **CI** (`ci.yml`): On push to `main` — backend build+test, frontend lint+typecheck+build, Docker build.
- **Release** (`release.yml`): On `v*` tag — publishes to Docker Hub (`voyvodka/webhook-engine`) and NuGet (`WebhookEngine.Sdk`).

### Pre-release checks (run locally before tagging)
```bash
dotnet build WebhookEngine.sln --configuration Release   # 0 errors, 0 warnings
dotnet test WebhookEngine.sln --no-build --configuration Release
cd src/dashboard && bun run lint && bun run typecheck && bun run build
```

### Release notes format
```
## WebhookEngine v{version}

{1-2 sentence summary}

### Features / Fixes / Changes
- **category:** description

### Quick Start (for major/minor releases)
docker pull + compose command

### Links
Docker Hub, NuGet, docs
```

**Never** include "Generated with Claude Code" or any AI-attribution line in release notes, PRs, commit messages, or any public-facing content.

### GitHub repo settings (keep in sync with releases)
- **Description**: matches the project summary
- **Homepage**: landing page (`webhook.sametozkan.com.tr`)
- **Topics**: webhook, dotnet, react, docker, postgresql, etc.
- **Releases**: every tag gets a detailed GitHub Release

Details: `docs/RELEASE.md`.
