## Project

**WebhookEngine** — Self-hosted webhook delivery platform.

Queue-based webhook delivery engine with retry logic, circuit breaker, HMAC signing, and a React dashboard for monitoring. .NET 10 backend, PostgreSQL for persistence and queue, SignalR for real-time updates. Self-hosted via Docker Compose.

**Core Value:** Reliable, observable webhook delivery — messages must reach their endpoints with guaranteed retry, proper signing, and full delivery visibility.

**Stack at a glance:** .NET 10 / ASP.NET Core / EF Core 10 / PostgreSQL 17 · React 19 / TypeScript 5.9 / Vite 7 / Tailwind 4 · SignalR · Serilog · OpenTelemetry + Prometheus · FluentValidation · xUnit / Testcontainers / FluentAssertions / NSubstitute. Exact versions live in the `.csproj` files and `src/dashboard/package.json`.

### Constraints

- **Stack lock:** .NET 10, React 19, PostgreSQL — no stack changes.
- **API:** No breaking changes; the `v1` prefix is preserved.
- **Package manager:** **Bun** for the frontend (overrides the global default of Yarn). NuGet for the backend. Never mix package managers.
- **PostgreSQL-only:** Redis/RabbitMQ/Kafka are out of scope. Queueing uses `SKIP LOCKED`; distributed locking uses advisory locks.
- **Single-process host:** API + Workers + Dashboard all run inside the same `WebApplication`.
- **Standard Webhooks spec:** Signature header names and format are fixed — no breaking changes.

### Documentation language rule

All Markdown files committed to git (root `*.md`, `docs/**`, `samples/**`, etc.) **must be in English**. Internal notes under `.planning/` are gitignored and may stay in Turkish. `CLAUDE.md` is committed → English.

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

## Architecture (overview)

```
src/
├─ WebhookEngine.Core/            # Entities, Enums, Interfaces, Options, Metrics — 0 NuGet deps
├─ WebhookEngine.Infrastructure/  # EF Core, PostgreSQL queue, repositories, services
├─ WebhookEngine.Worker/          # 5 BackgroundServices
├─ WebhookEngine.API/             # Controllers, middleware, SignalR, dashboard SPA host
├─ WebhookEngine.Sdk/             # .NET client (NuGet)
└─ dashboard/                     # React 19 SPA
```

**Workers:** `DeliveryWorker` (queue + delivery), `RetryScheduler` (Failed → Pending), `CircuitBreakerWorker` (Open → HalfOpen), `StaleLockRecoveryWorker` (release locks after worker crashes), `RetentionCleanupWorker` (purge old messages).

**Flow:** API → enqueue as `Pending` → `DeliveryWorker` dequeues with `FOR UPDATE SKIP LOCKED` → HMAC sign → HTTP POST → success: `Delivered`, fail+retry: `Failed`, max retries exhausted: `DeadLetter`. State transitions are guarded by `MessageStateMachine`.

Details: `docs/ARCHITECTURE.md`, `docs/DATABASE.md`, `docs/API.md`.

---

## Conventions

Detailed rules live in `.claude/rules/` — consult them while writing code.

| File | Covers |
|---|---|
| `.claude/rules/core-domain.md` | Entity / Enum / Interface / Options conventions |
| `.claude/rules/infrastructure.md` | EF Core, repositories, queue, advisory locks, migrations |
| `.claude/rules/backend-api.md` | Controllers, middleware, validators, SignalR, ApiEnvelope |
| `.claude/rules/workers.md` | BackgroundService pattern (scope-per-iteration, error backoff) |
| `.claude/rules/context7.md` | When and how to use Context7 for library/framework docs |

**Highlights:**
- C# 4-space indent; async methods end with `Async`; `CancellationToken ct = default` is the last parameter; PascalCase namespaces.
- React: PascalCase components, camelCase hooks/utilities, named exports, strict TypeScript.
- All API responses use `ApiEnvelope`; validation goes through FluentValidation.
- Read queries always use `.AsNoTracking()`.
- `WebhookMetrics? metrics = null` is the optional-dependency pattern.

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
