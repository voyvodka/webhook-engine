# v0.1.1 Stabilization Backlog

Patch release backlog for WebhookEngine v0.1.1. Scope limited to P0 and P1 issues discovered post-v0.1.0 release.

## Release Criteria

v0.1.1 is ready to cut when ALL of the following are true:

1. All P0 issues in scope are resolved and merged to main
2. All P1 issues in scope are resolved and merged to main
3. No regressions introduced (existing functionality unchanged)
4. `dotnet build` succeeds with zero warnings treated as errors
5. Docker Compose deployment starts cleanly (`docker compose up` exits healthy)
6. Dashboard loads and displays delivery data correctly

## Candidate Issues

Issues sourced from GitHub issue tracker. Priorities assigned per [triage-flow.md](triage-flow.md).

### P0 — Must Fix

| Issue | Title | Component | Status |
|-------|-------|-----------|--------|
| #1 | Post-release triage flow not established | `component:docs` | Open |
| #2 | v0.1.x stabilization backlog undefined — no patch release plan | `component:docs` | Open |

### P1 — Should Fix

| Issue | Title | Component | Status |
|-------|-------|-----------|--------|
| #3 | Application layer CQRS scaffold removal decision undocumented | `component:docs` | Open |

## v0.1.1 Scope (Confirmed)

The following issues are confirmed for v0.1.1:

- [ ] #1 — Post-release triage flow documented (`docs/triage-flow.md`)
- [ ] #2 — v0.1.1 stabilization backlog defined (`docs/backlog-v0.1.1.md`)
- [ ] #3 — CQRS scaffold removal ADR written (`docs/adr/adr-002-cqrs-scaffold-removal.md`)

## v0.2.0 Deferred

The following issues are deferred to v0.2.0 (P2 — workaround exists):

| Issue | Title | Reason for Deferral |
|-------|-------|---------------------|
| #4 | TypeScript SDK demand validation criteria | Demand threshold criteria can be defined later; .NET SDK is sufficient for current users |
| #5 | Webhook payload transformation technical proposal | New feature proposal; out of scope for stabilization patch |

## Go/No-Go Checklist

Before cutting v0.1.1 release:

- [ ] All P0 issues resolved and merged
- [ ] All P1 issues resolved and merged
- [ ] `dotnet build` clean (no errors, no new warnings)
- [ ] `docker compose -f docker-compose.yml up -d` starts all services
- [ ] Health check endpoints respond 200
- [ ] Dashboard overview page loads with real data
- [ ] Message delivery end-to-end test passes (send -> deliver -> status update)
- [ ] No open P0 issues remain in GitHub
- [ ] CHANGELOG.md updated with v0.1.1 entries
- [ ] Git tag `v0.1.1` created from main branch
