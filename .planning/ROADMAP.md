# Roadmap: WebhookEngine Stabilization

## Overview

Post-v0.1.0 stabilization milestone. Four phases ordered by risk: correctness defects first, then security gaps, then structural cleanup, then performance and future-facing decisions. Each phase delivers a verifiable capability before the next begins. No new features are built — only the engine is hardened.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Safety Foundations** - Correctness defects fixed: circuit breaker race condition eliminated, message status transitions guarded, stale lock recovery hardened, signing secret enforced at the data layer
- [ ] **Phase 2: Security Hardening** - Public endpoints rate-limited, replay scope threat model documented, idempotency window made configurable
- [ ] **Phase 3: Structural Cleanup** - CQRS scaffold removed, DashboardController split, DevTrafficGenerator decomposed, duplicate utility extracted; triage and stabilization backlog defined
- [ ] **Phase 4: Performance & Future Decisions** - Dashboard overview consolidated to single query, endpoint list N+1 eliminated, TypeScript SDK and payload transformation decisions documented

## Phase Details

### Phase 1: Safety Foundations
**Goal**: Delivery engine where status transitions are guarded and circuit state is never mutated during a read
**Depends on**: Nothing (first phase)
**Requirements**: CORR-01, CORR-02, CORR-03, CORR-04
**Success Criteria** (what must be TRUE):
  1. Concurrent workers processing the same endpoint cannot both transition circuit state from Open to HalfOpen simultaneously
  2. A message that reaches Delivered status cannot regress to Pending or Failed due to a subsequent exception in DeliveryWorker
  3. Creating an application or endpoint without a signing secret is rejected at the database level — null secrets cannot exist
  4. Stale lock recovery uses advisory locks or a dedicated lock_tokens table rather than a time-only heuristic
**Plans:** 1/3 plans executed

Plans:
- [ ] 01-01-PLAN.md — Circuit breaker race condition fix with advisory locks + CORR-04 stale lock verification
- [ ] 01-02-PLAN.md — Message status state machine to prevent Delivered-to-Pending regression
- [x] 01-03-PLAN.md — Endpoint SecretOverride CHECK constraint migration

### Phase 2: Security Hardening
**Goal**: Public endpoints protected against flooding, replay scope threat model resolved, idempotency window configurable per application
**Depends on**: Phase 1
**Requirements**: SECR-01, SECR-02, SECR-03
**Success Criteria** (what must be TRUE):
  1. Send and BatchSend endpoints reject requests from an AppId exceeding the configured token bucket threshold — excess calls receive 429
  2. Replay endpoint threat model is documented; resolution is either a confirmed backend guard or a recorded product decision explaining why UX-only mitigation is sufficient
  3. Idempotency deduplication window is read from application-level configuration rather than being hard-coded to 24 hours
**Plans**: TBD

### Phase 3: Structural Cleanup
**Goal**: Maintainable codebase with single-responsibility controllers, no dead scaffolding, no duplicate utilities, and triage/backlog process documented
**Depends on**: Phase 2
**Requirements**: REFR-01, REFR-02, REFR-03, REFR-04, GHIS-01, GHIS-02, GHIS-03
**Success Criteria** (what must be TRUE):
  1. DashboardController no longer exists as a single file — its responsibilities live in DashboardAnalyticsController, DashboardEndpointController, DashboardMessagesController, and DevTrafficController, with all original routes responding correctly
  2. Application layer contains no MediatR registration, no Commands/Queries folders, and no unused handler scaffolding; an ADR documents the removal decision
  3. DevTrafficGenerator is decomposed into EndpointTrafficProfiler and traffic scheduling components with no shared mutable lock state
  4. ResolveRateLimitPerMinute implementation exists in exactly one place (WebhookEngine.Core) with both prior call sites removed
  5. Post-release triage flow and v0.1.x stabilization backlog are defined with concrete v0.1.1 candidate list and release criteria
**Plans**: TBD
**UI hint**: yes

### Phase 4: Performance & Future Decisions
**Goal**: Dashboard loads from a single PostgreSQL round-trip, endpoint list has no N+1, and TypeScript SDK and payload transformation decisions are documented
**Depends on**: Phase 3
**Requirements**: PERF-01, PERF-02, GHIS-04, GHIS-05
**Success Criteria** (what must be TRUE):
  1. Dashboard Overview endpoint executes one SQL query using COUNT FILTER aggregation instead of nine separate CountAsync calls
  2. Endpoint list query uses a projection DTO and does not eager-load the EventTypes collection
  3. TypeScript SDK go/no-go criteria are defined and documented with a demand threshold — no ambiguity about what triggers the build decision
  4. Webhook payload transformation technical proposal exists as a written ADR with API contract, guardrails, and rollout plan

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Safety Foundations | 1/3 | In Progress|  |
| 2. Security Hardening | 0/? | Not started | - |
| 3. Structural Cleanup | 0/? | Not started | - |
| 4. Performance & Future Decisions | 0/? | Not started | - |
