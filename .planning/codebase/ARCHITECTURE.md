# Architecture

**Analysis Date:** 2026-03-30

## Pattern Overview

**Overall:** Layered + Clean Architecture (Domain-Driven Design foundations)

**Key Characteristics:**
- Clear separation between Core, Application, Infrastructure, API, and Worker layers
- Queue-based message processing with optimistic locking for distributed delivery
- Interface-driven design for service abstraction (IMessageQueue, IDeliveryService, ISigningService)
- Real-time delivery notifications via SignalR push to dashboard
- Entity Framework Core with PostgreSQL for persistence
- Background workers as hosted services for asynchronous processing

## Layers

**Core Layer:**
- Purpose: Domain models, business logic abstractions, and options
- Location: `src/WebhookEngine.Core/`
- Contains: Entities, Enums, Interfaces, Models, Metrics, Options
- Depends on: Nothing (no external dependencies)
- Used by: Application, Infrastructure, API, Worker layers

**Infrastructure Layer:**
- Purpose: Concrete implementations of interfaces, database operations, external service integration
- Location: `src/WebhookEngine.Infrastructure/`
- Contains: Database context (EF Core), repositories, queue implementation, delivery service, signing service, health tracking
- Depends on: Core
- Used by: API, Worker layers

**Application Layer:**
- Purpose: Application orchestration and use-case coordination
- Location: `src/WebhookEngine.Application/`
- Contains: MediatR registration (currently minimal, scaffolded structure with Commands/Queries folders)
- Depends on: Core, Infrastructure
- Used by: API layer

**API Layer:**
- Purpose: HTTP endpoints, middleware, controllers, authentication, real-time hubs
- Location: `src/WebhookEngine.API/`
- Contains: Controllers, middleware, SignalR hubs, validators, auth, startup code
- Depends on: Core, Infrastructure, Application, Worker
- Used by: HTTP clients
- Special: Hosts the React dashboard from `wwwroot/`

**Worker Layer:**
- Purpose: Background jobs and asynchronous processing
- Location: `src/WebhookEngine.Worker/`
- Contains: Background services (DeliveryWorker, RetryScheduler, CircuitBreakerWorker, RetentionCleanupWorker, StaleLockRecoveryWorker)
- Depends on: Core, Infrastructure
- Used by: API (registered as hosted services in DI)

**SDK Layer:**
- Purpose: .NET client library for consuming WebhookEngine API
- Location: `src/WebhookEngine.Sdk/`
- Contains: HTTP client wrappers, models, helpers
- Depends on: Nothing
- Used by: External applications

## Data Flow

**Send Message (Happy Path):**

1. Client calls `POST /api/v1/messages` with payload and endpoint selectors
2. MessagesController validates request via FluentValidation
3. Controller creates Message entity for each matching endpoint
4. Messages enqueued to PostgreSQL via IMessageQueue.EnqueueAsync()
5. API returns HTTP 202 Accepted with message IDs
6. DeliveryWorker polls queue with `SELECT ... FOR UPDATE SKIP LOCKED` (pessimistic locking per batch)
7. Worker acquires distributed lock by updating message status to "Sending" and setting LockedBy/LockedAt
8. Worker calls IDeliveryService.DeliverAsync() which posts payload to endpoint with HMAC signature
9. Worker records attempt in message_attempts table
10. On success: message status set to "Delivered", health tracker updates
11. On failure: message status reverts to "Pending" with scheduled retry
12. SignalRDeliveryNotifier pushes real-time status update to dashboard clients via DeliveryHub

**Retry Flow:**

1. RetryScheduler background worker periodically scans for "Pending" messages past their scheduledAt time
2. Resets any stale locks (messages locked > X minutes) back to "Pending" via ReleaseStaleLocksAsync()
3. Implements exponential backoff via RetryPolicyOptions configuration
4. Max retries defined per application or Message.MaxRetries = 7 default

**Circuit Breaker Flow:**

1. EndpointHealthTracker monitors delivery success/failure rates per endpoint
2. After consecutive failures (configurable), circuit breaker trips to "Open" state
3. CircuitBreakerWorker enforces cooldown period before attempting recovery
4. Messages to circuit-broken endpoints skip delivery and go straight to retry scheduling
5. CooldownUntil timestamp controls when circuit transitions to "HalfOpen" for recovery probing

**State Management:**

- **Message Status:** Pending → Sending → Delivered/Failed/DeadLettered
- **Endpoint Health Circuit State:** Closed → Open → HalfOpen → Closed
- **Distributed Locking:** LockedAt + LockedBy on Message entity prevents duplicate processing
- **Optimistic Concurrency:** Worker IDs included in lock to aid debugging; staleness detected by timestamp

## Key Abstractions

**IMessageQueue:**
- Purpose: Abstraction for message persistence and queue polling
- Examples: `src/WebhookEngine.Infrastructure/Queue/PostgresMessageQueue.cs`
- Pattern: Uses raw SQL with `FOR UPDATE SKIP LOCKED` for atomic dequeue operations without full locking

**IDeliveryService:**
- Purpose: Abstraction for webhook delivery (HTTP POST to endpoints)
- Examples: `src/WebhookEngine.Infrastructure/Services/HttpDeliveryService.cs`
- Pattern: Returns DeliveryResult with status code, latency, response body; truncates responses to 10KB max

**ISigningService:**
- Purpose: HMAC signature generation for webhook authentication
- Examples: `src/WebhookEngine.Infrastructure/Services/HmacSigningService.cs`
- Pattern: Implements Svix-compatible webhook signing with webhook-id, webhook-timestamp, webhook-signature headers

**IEndpointHealthTracker:**
- Purpose: Tracks endpoint health metrics and failure rates
- Examples: `src/WebhookEngine.Infrastructure/Services/EndpointHealthTracker.cs`
- Pattern: Singleton service tracking consecutive failures and last success/failure timestamps

**IEndpointRateLimiter:**
- Purpose: Rate limiting per endpoint to prevent overwhelming subscribers
- Examples: `src/WebhookEngine.Infrastructure/Services/EndpointRateLimiter.cs`
- Pattern: Singleton service; may use token bucket or sliding window algorithm

**IDeliveryNotifier:**
- Purpose: Real-time push notifications for delivery status updates
- Examples: `src/WebhookEngine.API/Hubs/DeliveryHub.cs` (SignalRDeliveryNotifier implementation)
- Pattern: SignalR-based; singleton scoped to allow workers to inject and notify dashboard

## Entry Points

**API Application:**
- Location: `src/WebhookEngine.API/Program.cs`
- Triggers: ASP.NET Core web host startup
- Responsibilities: Configure DI, database migrations, middleware pipeline, OpenTelemetry, background workers, static file serving for dashboard

**DeliveryWorker:**
- Location: `src/WebhookEngine.Worker/DeliveryWorker.cs`
- Triggers: Hosted service on startup; runs continuously in background
- Responsibilities: Poll queue, dequeue batches of pending messages, coordinate delivery attempts, update message status, notify subscribers

**RetryScheduler:**
- Location: `src/WebhookEngine.Worker/RetryScheduler.cs`
- Triggers: Hosted service on startup; runs on timer interval
- Responsibilities: Find messages past their scheduledAt time, re-enqueue for delivery

**CircuitBreakerWorker:**
- Location: `src/WebhookEngine.Worker/CircuitBreakerWorker.cs`
- Triggers: Hosted service on startup; runs periodically
- Responsibilities: Monitor endpoint health, trip/reset circuit breaker, enforce cooldown

**RetentionCleanupWorker:**
- Location: `src/WebhookEngine.Worker/RetentionCleanupWorker.cs`
- Triggers: Hosted service on startup; runs on schedule
- Responsibilities: Hard-delete old messages and attempts beyond retention period

**StaleLockRecoveryWorker:**
- Location: `src/WebhookEngine.Worker/StaleLockRecoveryWorker.cs`
- Triggers: Hosted service on startup; runs periodically
- Responsibilities: Detect and release locks held > configured duration to prevent message loss

## Error Handling

**Strategy:** Exception middleware wraps all requests; logs errors and returns structured API responses

**Patterns:**

- **Validation Errors:** FluentValidation validators run automatically via ASP.NET Core integration; return 400 Bad Request with details
- **Business Logic Errors:** Controllers return `UnprocessableEntity` (422) with error code and message in ApiEnvelope
- **Unhandled Exceptions:** ExceptionHandlingMiddleware catches and logs; returns 500 Internal Server Error
- **Queue Delivery Failures:** Caught in DeliveryWorker; logged but don't crash worker; message re-queued with retry logic
- **HTTP Timeouts:** HttpClient timeout set to 30 seconds (configurable); delivery marked failed and retried
- **Database Failures:** EF Core exceptions propagate; can trigger health check failure in containers

## Cross-Cutting Concerns

**Logging:** Serilog structured logging throughout; configured in Program.cs to read from appsettings.json; RequestLoggingMiddleware logs all HTTP requests/responses

**Validation:** FluentValidation fluent API for request validation; validators in `src/WebhookEngine.API/Validators/RequestValidators.cs`; auto-run on controller actions

**Authentication:** Cookie-based session auth for dashboard; API key auth via middleware (ApiKeyAuthMiddleware) for API endpoints; Bearer token format expected

**Metrics:** OpenTelemetry with Prometheus exporter; WebhookMetrics singleton tracks enqueues, deliveries, failures, attempts; `/metrics` endpoint exposes Prometheus format

**Security:** HMAC-SHA256 signing for outbound webhooks; password hashing with BCrypt for dashboard users; API key hashing stored in database

---

*Architecture analysis: 2026-03-30*
