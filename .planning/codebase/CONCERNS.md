# Codebase Concerns

**Analysis Date:** 2026-03-30

## Tech Debt

**Duplicate JSON Parsing Logic:**
- Issue: `ResolveRateLimitPerMinute()` is duplicated in both `DeliveryWorker.cs` and `DevTrafficGenerator.cs` with identical implementation
- Files: `src/WebhookEngine.Worker/DeliveryWorker.cs:263-292`, `src/WebhookEngine.API/Services/DevTrafficGenerator.cs:432-461`
- Impact: Maintenance burden; bug fixes must be applied twice; inconsistency risk
- Fix approach: Extract to shared utility class in `WebhookEngine.Core` or move to a shared service

**Large Monolithic Controller:**
- Issue: `DashboardController.cs` is 797 lines, handling 20+ endpoints for messages, endpoints, event types, dev traffic, timeline analytics, and replay functionality
- Files: `src/WebhookEngine.API/Controllers/DashboardController.cs`
- Impact: Difficult to test, maintain, and extend; mixing concerns (admin, dev tools, analytics)
- Fix approach: Split into `DashboardAnalyticsController`, `DashboardEndpointController`, `DevTrafficController` — one concern per controller

**Large Service Class:**
- Issue: `DevTrafficGenerator.cs` is 528 lines with endpoint selection logic, JSON parsing, rate limiting, and background loop management mixed together
- Files: `src/WebhookEngine.API/Services/DevTrafficGenerator.cs`
- Impact: Complex state management with `_stateLock`; difficult to unit test; multiple responsibilities
- Fix approach: Extract endpoint profiling logic into `EndpointTrafficProfiler` and traffic scheduling into separate class

## Performance Bottlenecks

**Dashboard Overview Query Multiplicity:**
- Problem: `DashboardController.Overview()` executes 8+ separate database queries (one per status count, health count, etc.) instead of a single aggregated query
- Files: `src/WebhookEngine.API/Controllers/DashboardController.cs:48-103`
- Cause: Individual `CountAsync()` calls for each message status and endpoint status instead of single grouped query
- Improvement path: Combine into single raw SQL query returning aggregated counts, similar to `Timeline()` endpoint (line 114-127)

**Unnecessary Relation Loading in List Views:**
- Problem: `EndpointRepository.ListAllAsync()` includes `EventTypes` collection for every endpoint, causing N+1 queries when rendering many endpoints
- Files: `src/WebhookEngine.Infrastructure/Repositories/EndpointRepository.cs:122-126`
- Cause: Eager loading of collections even when only count/summary needed in dashboard
- Improvement path: Create separate repository methods (`ListAllAsync()` vs `ListAllWithDetailsAsync()`) or use Select projection for specific fields

**Queue Timeline Aggregation:**
- Problem: The timeline endpoint uses raw SQL (line 114-127) which is good for performance, but only for this one query
- Files: `src/WebhookEngine.API/Controllers/DashboardController.cs:114-127`
- Cause: Other aggregations like `Overview()` don't follow same pattern
- Improvement path: Standardize on raw SQL for all time-series aggregations; add query caching

## Security Considerations

**Dashboard Replay Feature Missing Scope Validation:**
- Risk: `ReplayMessages()` endpoint (line 469+) allows dashboard admins to replay messages across any app without app ownership verification
- Files: `src/WebhookEngine.API/Controllers/DashboardController.cs:469-520`
- Current mitigation: Cookie-based admin authentication only
- Recommendations: Add application-level scoping to dashboard users or multi-tenant isolation; require explicit confirmation for bulk replay

**No Rate Limiting on Public API Endpoints:**
- Risk: `MessagesController.Send()` and `BatchSend()` endpoints can be flooded by authenticated API keys with no per-endpoint rate limits (only per-endpoint limits configured in metadata)
- Files: `src/WebhookEngine.API/Controllers/MessagesController.cs:31-100`
- Current mitigation: Only authenticated with valid API key; queue has soft limit (DevTrafficGenerator line 22)
- Recommendations: Add global request rate limiting per API key in middleware; implement token bucket at API level

**Signing Secret Validation Gaps:**
- Risk: In `DeliveryWorker.cs`, if both `endpoint.SecretOverride` and `endpoint.Application?.SigningSecret` are null, message is marked dead letter without attempt (line 145-150)
- Files: `src/WebhookEngine.Worker/DeliveryWorker.cs:145-150`
- Current mitigation: Validation happens at endpoint creation time
- Recommendations: Enforce signing secret presence at database level (NOT NULL constraint); add startup validation to fail fast

**Idempotency Key Window Hard-coded:**
- Risk: 24-hour idempotency window is hard-coded in `MessagesController.EnqueueSendRequestAsync()` (line 252)
- Files: `src/WebhookEngine.API/Controllers/MessagesController.cs:241-292`
- Current mitigation: None; hardcoded constant
- Recommendations: Make configurable per application or use DDD value object for idempotency policies

## Fragile Areas

**Circuit Breaker State Transitions:**
- Files: `src/WebhookEngine.Infrastructure/Services/EndpointHealthTracker.cs:85-106`
- Why fragile: State transitions happen during `GetCircuitStateAsync()` read operation (line 94-102), causing mutation during query. Race condition if multiple workers call simultaneously.
- Safe modification: Use explicit state transition method; lock on endpoint ID during state reads; or move transitions to write-only handler
- Test coverage: No explicit tests for race conditions during HalfOpen -> Open transitions

**Message Status Workflow State Machine:**
- Files: `src/WebhookEngine.Infrastructure/Repositories/MessageRepository.cs` (multiple status update methods), `src/WebhookEngine.Worker/DeliveryWorker.cs:150-230`
- Why fragile: Status transitions scattered across repository methods and worker; no centralized state machine. Possible to transition from Pending -> Delivered -> Failed -> Pending (line 235) after error catch
- Safe modification: Create explicit `MessageStatusTransition` validator; validate all transitions in repository before executing
- Test coverage: No systematic tests for invalid status transition paths

**Stale Lock Recovery Heuristic:**
- Files: `src/WebhookEngine.Core/Options/DeliveryOptions.cs:25`, worker lock detection logic
- Why fragile: Stale lock detection based only on time (5 minutes default). If clock skew, network issues, or slow database causes delays, locks may be incorrectly released or never released
- Safe modification: Add distributed lock with TTL using separate key, or use database lock advisory functions
- Test coverage: No tests for clock skew or lock recovery scenarios

## Scaling Limits

**Message Queue Polling Without Backpressure:**
- Current capacity: `BatchSize = 10` messages per poll interval (1 second default) = ~600 msg/min per worker
- Limit: With single `DeliveryWorker`, max throughput ~36k msg/hr. Multiple workers improve this, but no dynamic scaling or queue depth monitoring.
- Scaling path: Implement queue depth monitoring; trigger horizontal worker scaling; add batching strategy based on queue depth

**Database Connection Pool Exhaustion:**
- Current capacity: Default EF Core pool size (25 connections)
- Limit: Each worker thread creates scope with DbContext (line 49), holding connection through message processing (potentially 30+ seconds per message). At 10 msg/sec, connections exhaust quickly.
- Scaling path: Use connection pooling explicitly (`MaxPoolSize` config); implement async/await without holding connections; use unit-of-work pattern per message

**Timeline Aggregation Unbounded:**
- Current capacity: 30-day retention with 1-day bucketing = 30 buckets; fine for single query
- Limit: No pagination for timeline data; client receives all buckets for selected period. Large periods (7d with 5m intervals = 2016 buckets) may cause memory issues.
- Scaling path: Add server-side pagination for timeline; add downsampling for older data; pre-aggregate at fixed intervals

## Dependencies at Risk

**SignalR Real-time Notifications Without Fallback:**
- Risk: `DeliveryHub` (line 159 in Program.cs) used for real-time delivery updates; no fallback if WebSocket unavailable
- Impact: Dashboard won't receive live updates if SignalR fails; users won't see delivery status changes until refresh
- Migration plan: Add fallback to polling; implement circuit breaker for SignalR; add event sourcing for delivery events

**Entity Framework Core Migration Auto-Run:**
- Risk: `Database.MigrateAsync()` called on every startup (Program.cs line 140); no explicit migration versioning or rollback capability
- Impact: Failed migration blocks application startup; no easy rollback without manual database intervention
- Migration plan: Implement explicit migration runner with versioning; separate migration step from application startup; add pre-flight checks

## Missing Critical Features

**No Event Type Versioning:**
- Problem: Event type schemas stored as JSON but no versioning mechanism; can't evolve API contract without breaking existing subscriptions
- Blocks: Can't safely change event type structure without coordinating with all webhook subscribers
- Recommendation: Add `SchemaVersion` field to EventType; implement schema compatibility checking before message delivery

**No Webhook Signature Verification Client Validation:**
- Problem: System generates signatures, but no built-in validation helper for webhook consumers
- Blocks: Customer SDKs must implement own signature validation; testing webhook handlers harder
- Recommendation: Provide reference implementations of signature validation in popular languages; add validation helper CLI

**Limited Error Detail Visibility:**
- Problem: Delivery failures recorded with generic error types (line 182 in DeliveryWorker: "Timeout", "Failed") without error categorization
- Blocks: Can't distinguish between transient errors (timeout) vs permanent errors (invalid URL) vs client errors (malformed signature)
- Recommendation: Add error categorization enum; store root cause details; implement error-specific retry strategies

## Test Coverage Gaps

**No Integration Tests for Delivery Workflow:**
- What's not tested: Full message flow from creation to delivery to status updates with actual HTTP calls
- Files: `tests/WebhookEngine.Infrastructure.Tests/Repositories/` has unit tests but no integration tests for delivery
- Risk: Bugs in message lifecycle (Pending -> Sending -> Delivered) undetected; retry logic untested; status corruption possible
- Priority: High — core business logic

**Missing Circuit Breaker Integration Tests:**
- What's not tested: State transitions between Closed -> Open -> HalfOpen -> Closed under concurrent load; race conditions during state changes
- Files: `src/WebhookEngine.Infrastructure/Services/EndpointHealthTracker.cs` — only entity-level health tests exist
- Risk: Silent failures when circuit breaker doesn't trigger or gets stuck; messages silently dropped
- Priority: High — critical reliability feature

**No Dashboard API Tests:**
- What's not tested: DashboardController endpoints (797 lines) have no apparent test coverage; timeline aggregation logic untested
- Files: `src/WebhookEngine.API/Controllers/DashboardController.cs` — no corresponding test file visible
- Risk: Regression in dashboard analytics; incorrect aggregate calculations undetected
- Priority: Medium — impacts observability but not core delivery

**Lack of Idempotency Tests:**
- What's not tested: Duplicate requests with same idempotency key; idempotency window edge cases (at 24h boundary)
- Files: `src/WebhookEngine.API/Controllers/MessagesController.cs:247-262` — logic exists but no test coverage apparent
- Risk: Duplicate messages delivered despite idempotency key; business logic bugs undetected
- Priority: Medium — can cause duplicate side effects

---

*Concerns audit: 2026-03-30*
