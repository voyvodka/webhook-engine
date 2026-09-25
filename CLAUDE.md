# CLAUDE.md — WebhookEngine

Self-hosted webhook delivery platform. A single ASP.NET Core host serves the REST API, runs the background delivery workers and serves the React dashboard; PostgreSQL is both the data store and the job queue. Core value: reliable, observable delivery — every message reaches its endpoint with guaranteed retry, correct signing and full delivery visibility. Shipped as Docker Compose (app + postgres), MIT licensed.

## Stack

Exact versions live in the `.csproj` files and `src/dashboard/package.json`.

| Layer | Choice | Why |
|---|---|---|
| Backend | C# / .NET 10, ASP.NET Core Controllers | Business logic lives in controllers; the Application/CQRS layer was removed (ADR-002) |
| Data + queue | EF Core 10, PostgreSQL 17+ | One dependency covers storage, the queue (`SKIP LOCKED`) and locking (advisory locks) |
| Workers | `BackgroundService`s in the API host | No separate worker deployment |
| Real-time | SignalR (`/hubs/deliveries`) | Live delivery and endpoint-health updates without dashboard polling |
| Dashboard | React 19, TypeScript 6, Vite 8, Tailwind 4, TanStack Query 5, React Router 8, Recharts, Lucide | Built into `API/wwwroot` and served as static files |
| Validation / logging | FluentValidation, Serilog (JSON) | Structured logs with correlation ids |
| Observability | OpenTelemetry + Prometheus exporter | `/metrics`, optional bearer scrape token |
| Tests | xUnit, FluentAssertions, NSubstitute, Testcontainers | Real PostgreSQL for anything the database decides |
| Clients | `WebhookEngine.Sdk` (NuGet), `@webhookengine/endpoint-manager` (npm) | Sender SDK and embeddable customer portal UI |

## Constraints

- **Stack lock:** .NET 10, React 19, PostgreSQL. No stack changes.
- **PostgreSQL only:** no Redis, RabbitMQ or Kafka. Queueing is `FOR UPDATE SKIP LOCKED`; distributed locking is advisory locks.
- **Single process:** API, workers and dashboard run in the same `WebApplication`.
- **API:** no breaking changes; the `v1` prefix is immutable.
- **Standard Webhooks:** signature header names and format are fixed.
- **Migrations** are applied on startup. Never generate a migration or run any `dotnet ef` command without explicit user consent.
- **Package managers:** Bun for the frontend (never npm, yarn or pnpm), NuGet for the backend. Never mix.
- **Zero-dependency packages:** `WebhookEngine.Core` takes no NuGet references; `WebhookEngine.Sdk` takes no external NuGet dependencies.
- **Tests never mock the persistence layer** (a mock-vs-prod divergence once masked migration bugs): no substitute for `WebhookDbContext`, `DbSet`, a repository or a migration. Anything PostgreSQL decides — `SKIP LOCKED`, advisory locks, unique constraints, `ExecuteUpdate` compare-and-set guards — is tested against real PostgreSQL (Testcontainers, `Infrastructure.Tests`). The EF InMemory provider is fine for HTTP-pipeline, middleware and validator tests, and pure-domain interfaces such as `IMessageQueue` may be substituted.
- **No AI attribution** ("Generated with Claude Code", `Co-Authored-By` trailers or similar) in commits, PRs, release notes or any public-facing content.
- **Documentation language:** every Markdown file committed to git (root `*.md` including this one, `docs/**`, `samples/**`) is English. Only the gitignored `.planning/` may be Turkish.

## Working notes — `.planning/`

Gitignored, never public. At the start of a session read, in order: `.planning/ROADMAP.md` (where the project is and is heading), `TODO.md` (active and upcoming tasks), `NOTES.md` (decision archive, design sketches, known tech debt), `README.md` (how the folder is used).

Keep it fresh: strike a task from `TODO.md` as soon as it lands, drop new decisions or learnings into `NOTES.md`, and at phase transitions update `.planning/ROADMAP.md` and sync the public `docs/ROADMAP.md`. External-facing changes go to `docs/` and `docs/adr/`; `.planning/` is for day-to-day work.

## Commands

```bash
docker compose -f docker/docker-compose.dev.yml up -d      # PostgreSQL only, for local dev
dotnet build WebhookEngine.sln
dotnet run --project src/WebhookEngine.API                 # API + workers + dashboard on :5128
dotnet test WebhookEngine.sln                              # Testcontainers suites need Docker
dotnet test tests/WebhookEngine.Core.Tests --filter "FullyQualifiedName~ClassName.MethodName"
dotnet test --filter "DisplayName~circuit_breaker"
cd src/dashboard && bun install && bun run dev             # Vite on :5173, proxies the API
cd src/dashboard && bun run lint && bun run typecheck && bun run build   # build → API/wwwroot
docker compose -f docker/docker-compose.yml up             # production shape: app + postgres
```

## Architectural decisions that bind every change

Rationale lives in `docs/ARCHITECTURE.md` (§1 key decisions, §3 components, §7 technology choices) and `docs/adr/`.

1. **Delivery flow.** The API enqueues `Pending` → `DeliveryWorker` claims with `FOR UPDATE SKIP LOCKED` → HMAC sign → HTTP POST → `Delivered`; a failure with retries left becomes `Failed`, exhausted retries become `DeadLetter`. Transitions are guarded by `MessageStateMachine` and the `MessageRepository.Mark*Async` compare-and-set guards.
2. **At-least-once delivery.** A message may be delivered more than once; it is never lost.
3. **Signing** is HMAC-SHA256 per the [Standard Webhooks](https://www.standardwebhooks.com/) spec.
4. **Retry policy:** 7 attempts, exponential backoff 5s, 30s, 2m, 15m, 1h, 6h, 24h.
5. **Circuit breaker** per endpoint: 5 consecutive failures open it, 5-minute cooldown before HalfOpen. Endpoint-health mutations are serialized with PostgreSQL advisory locks.
6. **API keys:** `whe_{appIdShort}_{random32}`, stored as a SHA256 hash with a prefix for lookup, shown once.
7. **Outbound HTTP** goes only through the named `webhook-delivery` client, whose `ConnectCallback` pins the resolved IP (SSRF and DNS-rebinding defense). Never `new HttpClient()`.
8. **Scale path.** A single instance targets 100–500 deliveries/s; sustained >1000/s means swapping the queue behind `IMessageQueue`, not adding a broker beside it.
9. **Bundled SPA.** The dashboard ships inside the API image; there is no separate frontend deployment.

## Code conventions that apply everywhere

- **C#:** 4-space indent; PascalCase namespaces, types, methods and constants; `_camelCase` private fields; `I`-prefixed interfaces; one file per class, named after it; `Async` suffix on async methods; `CancellationToken ct = default` as the last parameter and passed through the whole chain; constructor injection only, no service locator.
- **Comments are sparing.** Default to none. Add one only when the *why* is non-obvious: a load-bearing ordering, a hidden invariant, a workaround for a known bug. Never restate what the code does, never reference the current PR, task or caller, never grow a one-line reason into a paragraph. If deleting the comment would not confuse a future reader, it should not be there. The same holds for commit messages, YAML, JSON and `# this does X` headers.

## Workflow

Contributor-facing steps are in `CONTRIBUTING.md`; release mechanics are in `docs/RELEASE.md`.

**Issues.** New issues start as `status: needs-triage` and are triaged before any implementation: repro, exactly one of `priority: p0|p1|p2`, component labels (`api`, `dashboard`, `worker`, `sdk`, `infrastructure`, `database`, `security`, `performance`), a milestone, then `status: triaged`. Add `regression` when the behavior worked in an earlier release.

**Branches and PRs.** `main` is protected: feature branch → PR → green CI → squash-merge. Direct push is reserved for trivial admin overrides. Branch names are `feature/`, `fix/`, `refactor/`, `docs/` or `chore/` plus a short slug; `dependabot/...` branches keep their names. Head branches are deleted on merge and the clone has `fetch.prune=true`, so after a merge: `git checkout main && git pull --ff-only origin main`, then delete local branches whose upstream is `[gone]`.

**Labels.** Every PR carries one type label and every area label that applies; release notes are grouped by them.
- Type: `enhancement`, `bug`, `security` (CodeQL, secret scanning, CVE, hardening), `performance` (measurable), `regression`, `documentation` (`docs/`, README, CHANGELOG, ADRs, `CLAUDE.md` / `AGENTS.md`), `dependencies`.
- Area: `api`, `worker`, `infrastructure`, `database` (migrations, schema, indexes, raw SQL), `dashboard`, `sdk` (including `samples/signature-verification/`), `ci`, `docker`, `nuget`, `npm`.
- Priority and `status:` labels go on issues, not PRs. `good first issue` / `help wanted` only on issues open for contribution.

**Commits and issue links.** Conventional Commits (`feat:`, `fix:`, `docs:`, `refactor:`, `chore:`, …). Use `Refs #n` while work is in progress and `Closes #n` in the PR body (or the final commit reaching `main`) once the change is complete. Never close an issue before the fix is visible on the remote; if it should close only after a patch release, use `Refs` and close it by hand once the tag is out. One issue per fix; do not bundle unrelated closures.

**CHANGELOG.** Keep a Changelog. When merging, add the PR's user-facing summary under `## [Unreleased]` in the section matching its type label (`Added`, `Changed`, `Fixed`, `Removed`, `Security`).

**Open-work analysis.** At the start of a development cycle, or when asked, review open issues (status, priority, component, milestone), open PRs with their review and check state, recent workflow failures and pending release actions, then publish a short **Required Next Actions** list ordered P0 → P2 with owner, action and output.

**Releases.** SemVer with `v`-prefixed tags. CI (`ci.yml`) builds and tests backend, frontend and Docker on every push to `main`; a `v*` tag publishes the Docker image (`voyvodka/webhook-engine`) and `WebhookEngine.Sdk` to NuGet, a `portal-v*` tag publishes `@webhookengine/endpoint-manager`. Before tagging: `dotnet build WebhookEngine.sln -c Release` with 0 errors and 0 warnings, `dotnet test WebhookEngine.sln --no-build -c Release`, and a clean dashboard lint, typecheck and build. GitHub Release notes follow: `## WebhookEngine v{version}`, a 1–2 sentence summary, `### Features / Fixes / Changes` bullets as `**category:** description`, a Quick Start (docker pull + compose) for major and minor releases, and Links (Docker Hub, NuGet, docs). Every tag gets a GitHub Release; keep the repo description, homepage (`webhook.sametozkan.com.tr`) and topics in sync.

## Where the truth lives

| Question | Source |
|---|---|
| Components, delivery pipeline, queue, signing, circuit breaker, SSRF, portal auth, solution layout | `docs/ARCHITECTURE.md` |
| Endpoints, response envelope, error codes, SignalR events, webhook headers, rate limits | `docs/API.md` |
| Schema, indexes, retention, key queries | `docs/DATABASE.md` |
| Embeddable customer portal | `docs/PORTAL.md` |
| Requirements and non-functional targets (latency, idle memory, dashboard load) | `docs/PRD.md` |
| Benchmarks and optimization log | `docs/PERFORMANCE.md` |
| Setup, configuration, tuning, operations | `docs/GETTING-STARTED.md`, `docs/SELF-HOSTING.md` |
| Release procedure and troubleshooting | `docs/RELEASE.md` |
| Public roadmap | `docs/ROADMAP.md` |
| Decisions and their revisit triggers | `docs/adr/` |
| Middleware order (load-bearing) | `src/WebhookEngine.API/Program.cs` |
| Metric names | `src/WebhookEngine.Core/Metrics/WebhookMetrics.cs` |

Per-area coding rules live in `.claude/rules/` and load automatically when a file matching their `paths:` is read. They are local only (`.claude/` is gitignored): `core-domain.md` (entities, enums, interfaces, options), `infrastructure.md` (EF Core, repositories, queue, advisory locks, migrations, HTTP client), `backend-api.md` (controllers, middleware, validators, SignalR, auth), `workers.md`, `dashboard.md`, `sdk.md`, `testing.md`.

## Subagents

Specialist subagents live in `.claude/agents/` (local, not in the repository). They are read-only consultants — the main session is the only writer — and each file's `description` says when to call it. Call `reviewer` before merging significant changes.
