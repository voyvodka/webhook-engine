# WebhookEngine

## What This Is

Queue-based webhook delivery engine with retry logic, circuit breaker, HMAC signing, and a React dashboard for monitoring. .NET 10 backend, PostgreSQL for persistence and queue, SignalR for real-time updates. Self-hosted via Docker Compose.

## Core Value

Reliable, observable webhook delivery — messages must reach their endpoints with guaranteed retry, proper signing, and full delivery visibility.

## Requirements

### Validated

- ✓ Queue-based message delivery with PostgreSQL polling — existing
- ✓ Exponential backoff retry with configurable max retries — existing
- ✓ Circuit breaker per endpoint (Closed/Open/HalfOpen) — existing
- ✓ HMAC-SHA256 webhook signing (Svix-compatible) — existing
- ✓ Per-endpoint rate limiting — existing
- ✓ REST API for applications, endpoints, event types, messages — existing
- ✓ API key authentication for webhook operations — existing
- ✓ Cookie-based dashboard authentication — existing
- ✓ React dashboard with real-time delivery monitoring via SignalR — existing
- ✓ Prometheus metrics and OpenTelemetry instrumentation — existing
- ✓ Message idempotency (24h window) — existing
- ✓ Retention cleanup and stale lock recovery workers — existing
- ✓ .NET SDK client library — existing
- ✓ Docker Compose deployment — existing
- ✓ Dev traffic generator for testing — existing

### Active

- [ ] Duplicate code elimination (ResolveRateLimitPerMinute, large controller/service splits)
- [ ] Dashboard query performance optimization (N+1, multi-query aggregation)
- [ ] API rate limiting on public endpoints
- [ ] Dashboard replay scope validation (multi-tenant isolation)
- [ ] Signing secret enforcement (NOT NULL constraint)
- [ ] Configurable idempotency window
- [ ] Circuit breaker race condition fix (mutation during query)
- [ ] Message status state machine centralization
- [ ] Post-release triage flow (GitHub #1)
- [ ] v0.1.x stabilization backlog (GitHub #2)
- [ ] Application layer CQRS vs scaffold removal decision (GitHub #3)
- [ ] TypeScript SDK demand validation (GitHub #4)
- [ ] Webhook payload transformation proposal (GitHub #5)

### Out of Scope

- Test coverage expansion — user explicitly excluded from this milestone
- New feature implementation (payload transformation itself) — only the proposal, not the build
- TypeScript SDK implementation — only demand validation criteria
- Mobile app or alternative frontends — web dashboard only

## Context

- Project is post-v0.1.0 release with known tech debt and stability concerns
- Codebase map already completed (`.planning/codebase/`)
- 5 open GitHub issues (2x P0, 1x P1, 2x P2) all included
- DashboardController is 797 lines handling 20+ endpoints — primary refactoring target
- DevTrafficGenerator is 528 lines with mixed responsibilities
- Circuit breaker has race condition in state transitions during read operations
- Message status transitions scattered across repository and worker — no centralized state machine

## Constraints

- **Tech stack**: .NET 10, React 19, PostgreSQL — no stack changes
- **Breaking changes**: Avoid API breaking changes; this is a patch-level stabilization
- **Release**: Main branch stabilization; release cut planned separately
- **Package manager**: Yarn for frontend, NuGet for backend

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Tech debt + performance + security in single milestone | User wants comprehensive stabilization before new features | — Pending |
| GitHub issues included in same milestone | Keeps all stabilization work together | — Pending |
| Test coverage excluded | User wants to focus on fixes first, tests later | — Pending |
| Main branch stability as goal (not release) | Release cut is separate concern | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd:transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd:complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-03-30 after initialization*
