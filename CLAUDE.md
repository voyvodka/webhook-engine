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
| `.claude/rules/workers.md` | `BackgroundService` pattern (scope-per-iteration, error backoff) |
| `.claude/rules/dashboard.md` | React 19 + Vite 7 + Tailwind 4 + Bun conventions, SignalR client, lazy routes |
| `.claude/rules/sdk.md` | `WebhookEngine.Sdk` public surface, `WebhookVerifier`, NuGet metadata |
| `.claude/rules/testing.md` | xUnit + Testcontainers (no mocked DB), race-condition tests, naming |
| `.claude/rules/context7.md` | When and how to use Context7 for library / framework docs |

**Highlights:**
- C# 4-space indent; async methods end with `Async`; `CancellationToken ct = default` is the last parameter; PascalCase namespaces.
- React: PascalCase components, camelCase hooks / utilities, named exports (default export only for lazy-loaded pages), strict TypeScript.
- All API responses use `ApiEnvelope`; validation goes through FluentValidation.
- Read queries always use `.AsNoTracking()`.
- Status transitions on `Message` go through `MessageRepository.Mark*Async` CAS guards (`WHERE LockedBy = @lockedBy AND Status = Sending`); callers must check the `bool` result.
- `EndpointHealth` mutations go through `EndpointHealthTracker.WithEndpointLockAsync` (advisory-lock namespace `100_001`).
- HTTP delivery uses the named `webhook-delivery` client only — never `new HttpClient()`. `SocketsHttpHandler.ConnectCallback` pins resolved IPs (DNS-rebinding defense).
- `WebhookMetrics? metrics = null` is the optional-dependency pattern.

---

## Agents

Specialist subagents live in `.claude/agents/`. Each one owns a domain — call the right one before you start work in that domain so the agent can apply its rules from the first read of the code.

| Agent | When to call |
|---|---|
| `dotnet-api-expert` | Any change to controllers, middleware, validators, request / response DTOs, SignalR hub events, OpenAPI / Scalar surface, or anything crossing `/api/v1/*`. The `v1` prefix is immutable. |
| `dotnet-engine-expert` | Any change to the 5 `BackgroundService`s, the PostgreSQL queue, `MessageStateMachine`, advisory-lock circuit breaker, HMAC signing pipeline, or the `webhook-delivery` HttpClient. |
| `infrastructure-expert` | Any change to `WebhookDbContext`, repositories, migrations, partial indexes, or advisory-lock namespaces. **Migrations are never auto-generated** — they require explicit user consent. |
| `dashboard-expert` | Any React component / page / hook / route / api-client change. Bun-only (overrides the global Yarn default). Build output ships to `WebhookEngine.API/wwwroot/`. |
| `sdk-expert` | Any change inside `src/WebhookEngine.Sdk/`. The package targets `net10.0` only; zero external NuGet dependencies; `WebhookVerifier` uses `CryptographicOperations.FixedTimeEquals`. |
| `release-manager` | SemVer tags, CHANGELOG releases, Docker Hub multi-arch publish, NuGet publish, GitHub Releases, repo-settings sync. NEVER includes any AI-attribution line in public-facing artifacts. |
| `test-expert` | Any test change. xUnit + FluentAssertions + NSubstitute + Testcontainers. **Never mocks the database** (project rule from a past mock-vs-prod incident). |
| `opensource-guardian` | `.github/workflows/`, `.github/dependabot.yml`, repo labels, branch-protection settings, license decisions, CodeQL / Dependency Review triage, CVE response. |
| `reviewer` | Read-only quality gate. Call before merging significant changes, when writing an ADR, or when classifying a breaking change. Outputs verdict + punch list — never edits code. |

Built-in helpers (always available):
- `Explore` — fast read-only search agent for locating code (single targeted lookup → "very thorough" multi-location).
- `Plan` — software-architect agent for designing implementation strategies before coding.
- `general-purpose` — open-ended research / multi-step tasks not covered by a domain agent.

When two agents overlap (e.g., a controller change that also adds a repository method), call the agent whose domain owns the **primary** concern; the agent will coordinate with the others (the `Before you do anything` section in each agent file lists its peer dependencies). For any change that touches `main` directly (rare — admin override only), still pair with `reviewer` before pushing.

---

## PR Workflow & Labels

`main` is protected — direct push is reserved for trivial admin overrides. Anything else flows through a feature branch + PR + green CI + squash-merge. The repo deletes head branches automatically on merge.

### Branch naming
- `feature/<short-slug>` — new functionality (`feature/dashboard-logo`)
- `fix/<short-slug>` — bug or security fix (`fix/codeql-log-forging-and-pii`)
- `refactor/<short-slug>` — internal restructuring with no behavior change
- `docs/<short-slug>` — docs-only or repo-meta updates (`docs/pr-label-policy`)
- `chore/<short-slug>` — config / tooling tweaks
- `dependabot/...` — created automatically; do not rename

### Post-merge cleanup (remote + local)

The repo has `delete_branch_on_merge=true` enabled, so merged head branches disappear from `origin` automatically. To stay in sync locally, the repo's `.git/config` carries `fetch.prune=true`, which means **every** `git fetch` / `git pull` removes stale `origin/...` tracking refs in one step. After a PR merges, run:

```bash
git checkout main && git pull --ff-only origin main
git for-each-ref --format='%(refname:short) %(upstream:track)' refs/heads \
  | awk '$2=="[gone]" && $1!="main" {print $1}' \
  | xargs -r git branch -D
```

The `awk` line drops every local branch whose upstream has gone away (the merged feature branch). Runs as a no-op when there is nothing to clean.

### Required labels per PR
Every PR carries **at least one type label** and **at least one area label** so the changelog can be grouped at release time. Apply via `gh pr edit <n> --add-label <label>` or the PR sidebar.

**Type (pick one):**
| Label | When |
|---|---|
| `enhancement` | New user-visible feature or improvement |
| `bug` | Defect fix that restores intended behavior |
| `security` | Resolves a CodeQL/secret-scanning/CVE alert or hardens a vulnerability |
| `performance` | Measurable latency / throughput / memory improvement |
| `regression` | Reverts or repairs a behavior previously working |
| `documentation` | Public docs (`docs/`, `README`, `CHANGELOG`, ADRs) or `CLAUDE.md` |
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

### Release-note grouping
The Unreleased section of `CHANGELOG.md` mirrors the type labels (`### Added` / `### Changed` / `### Fixed` / `### Removed` / `### Security`). When merging, append the PR's user-facing summary under the section matching its type label.

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
