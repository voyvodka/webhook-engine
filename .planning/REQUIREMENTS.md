# Requirements: WebhookEngine Stabilization

**Defined:** 2026-03-30
**Core Value:** Reliable, observable webhook delivery — messages must reach their endpoints with guaranteed retry, proper signing, and full delivery visibility.

## v1 Requirements

Requirements for stabilization milestone. Each maps to roadmap phases.

### Correctness

- [x] **CORR-01**: Circuit breaker state transitions use advisory locks to prevent race conditions during concurrent worker access
- [x] **CORR-02**: Message status transitions enforce valid paths via centralized IMessageStateMachine (prevent Delivered→Pending regression)
- [x] **CORR-03**: Signing secret enforced as NOT NULL at database level with data backfill migration for existing null rows
- [x] **CORR-04**: Stale lock recovery uses advisory locks or dedicated lock_tokens table instead of time-only heuristic

### Security

- [x] **SECR-01**: API rate limiting on public endpoints using built-in token bucket middleware partitioned by AppId
- [x] **SECR-02**: Dashboard replay endpoint validates application ownership before allowing message replay
- [x] **SECR-03**: Idempotency window is configurable per application instead of hard-coded 24 hours

### Refactoring

- [ ] **REFR-01**: DashboardController split into DashboardAnalyticsController, DashboardEndpointController, DashboardMessagesController, and DevTrafficController
- [x] **REFR-02**: Application layer CQRS scaffold removed — empty MediatR handlers and Commands/Queries folders cleaned up, ADR documented
- [ ] **REFR-03**: DevTrafficGenerator decomposed into EndpointTrafficProfiler and traffic scheduling components
- [x] **REFR-04**: Duplicate ResolveRateLimitPerMinute extracted to shared utility in WebhookEngine.Core

### Performance

- [ ] **PERF-01**: Dashboard Overview endpoint consolidated from 9 separate SQL queries to single aggregated query using COUNT FILTER
- [ ] **PERF-02**: Endpoint list query uses Select projection DTO instead of eager loading EventTypes collection

### GitHub Issues

- [x] **GHIS-01**: Post-release triage flow established with reproducibility marking, component labels, and priority assignment (GitHub #1)
- [x] **GHIS-02**: v0.1.x stabilization backlog defined with concrete v0.1.1 candidate list and release criteria (GitHub #2)
- [x] **GHIS-03**: Application layer CQRS vs scaffold removal decision documented as ADR with migration approach (GitHub #3)
- [ ] **GHIS-04**: TypeScript SDK demand validation criteria defined with go/no-go threshold (GitHub #4)
- [ ] **GHIS-05**: Webhook payload transformation technical proposal with API contract, guardrails, and rollout plan (GitHub #5)

## v2 Requirements

Deferred to future milestone. Tracked but not in current roadmap.

### Testing

- **TEST-01**: Integration tests for full message delivery workflow
- **TEST-02**: Circuit breaker integration tests for concurrent state transitions
- **TEST-03**: Dashboard API endpoint tests
- **TEST-04**: Idempotency edge case tests

### Features

- **FEAT-01**: TypeScript SDK implementation (pending demand validation)
- **FEAT-02**: Webhook payload transformation implementation (pending proposal)
- **FEAT-03**: Event type versioning with schema compatibility checking

## Out of Scope

| Feature | Reason |
|---------|--------|
| Test coverage expansion | User explicitly excluded from this milestone — separate effort |
| TypeScript SDK implementation | Only demand validation criteria, not build |
| Payload transformation build | Only technical proposal, not implementation |
| Event type versioning | Missing critical feature but not stabilization priority |
| Mobile app / alternative frontends | Web dashboard only |
| Stack changes | .NET 10, React 19, PostgreSQL locked |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| CORR-01 | Phase 1 | Complete |
| CORR-02 | Phase 1 | Complete |
| CORR-03 | Phase 1 | Complete |
| CORR-04 | Phase 1 | Complete |
| SECR-01 | Phase 2 | Complete |
| SECR-02 | Phase 2 | Complete |
| SECR-03 | Phase 2 | Complete |
| REFR-01 | Phase 3 | Pending |
| REFR-02 | Phase 3 | Complete |
| REFR-03 | Phase 3 | Pending |
| REFR-04 | Phase 3 | Complete |
| GHIS-01 | Phase 3 | Complete |
| GHIS-02 | Phase 3 | Complete |
| GHIS-03 | Phase 3 | Complete |
| PERF-01 | Phase 4 | Pending |
| PERF-02 | Phase 4 | Pending |
| GHIS-04 | Phase 4 | Pending |
| GHIS-05 | Phase 4 | Pending |

**Coverage:**
- v1 requirements: 18 total
- Mapped to phases: 18
- Unmapped: 0 ✓

---
*Requirements defined: 2026-03-30*
*Last updated: 2026-03-30 after roadmap creation — all requirements mapped*
