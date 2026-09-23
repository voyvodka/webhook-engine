# CLAUDE.md

Project rules, architecture, conventions, and the PR/release workflow live in `AGENTS.md`, shared with
other coding agents. This file adds only what is specific to Claude Code.

@AGENTS.md

---

## Agents

Specialist subagents live in `.claude/agents/`. Each one owns a domain — call the right one before you start work in that domain so the agent can apply its rules from the first read of the code.

| Agent | When to call |
|---|---|
| `dotnet-api-expert` | Any change to controllers, middleware, validators, request / response DTOs, SignalR hub events, OpenAPI / Scalar surface, or anything crossing `/api/v1/*`. The `v1` prefix is immutable. |
| `dotnet-engine-expert` | Any change to the `BackgroundService`s, the PostgreSQL queue, `MessageStateMachine`, advisory-lock circuit breaker, HMAC signing pipeline, or the `webhook-delivery` HttpClient. |
| `infrastructure-expert` | Any change to `WebhookDbContext`, repositories, migrations, partial indexes, or advisory-lock namespaces. **Migrations are never auto-generated** — they require explicit user consent. |
| `dashboard-expert` | Any React component / page / hook / route / api-client change. Bun-only. Build output ships to `WebhookEngine.API/wwwroot/`. |
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

The per-area rules in `.claude/rules/` (listed under Conventions in `AGENTS.md`) load automatically in Claude Code.
