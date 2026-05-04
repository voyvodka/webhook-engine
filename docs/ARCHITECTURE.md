# Architecture Document
# WebhookEngine

---

## 1. High-Level Architecture

```
                                ┌─────────────────────────────────────────┐
                                │           WebhookEngine Host            │
                                │         (Single ASP.NET Core App)       │
                                │                                         │
  [Your SaaS App]               │  ┌────────────┐    ┌──────────────────┐ │
       │                        │  │ REST API   │    │  React Dashboard │ │
       │  POST /api/v1/...      │  │ Controllers│    │  (SPA, served    │ │
       ├───────────────────────►│  │            │    │   as static)     │ │
       │                        │  └─────┬──────┘    └────────┬─────────┘ │
       │                        │        │                    │           │
       │                        │        ▼                    │           │
       │                        │  ┌───────────┐              │           │
       │                        │  │  Service  │◄─────────────┘           │
       │                        │  │   Layer   │                          │
       │                        │  └─────┬─────┘                          │
       │                        │        │                                │
       │                        │        ▼                                │
       │                        │  ┌───────────┐     ┌──────────────────┐ │
       │                        │  │    EF     │     │  Delivery Worker │ │
       │                        │  │   Core    │     │  (Background     │ │
       │                        │  │           │     │   Service)       │ │
       │                        │  └─────┬─────┘     └────────┬─────────┘ │
       │                        │        │                    │           │
       │                        │        ▼                    ▼           │
       │                        │  ┌─────────────────────────────────┐    │
       │                        │  │           PostgreSQL            │    │
       │                        │  │  (data + queue + delivery log)  │    │
       │                        │  └─────────────────────────────────┘    │
       │                        │                                         │
  [Webhook Endpoints]◄──────────│──── HTTP POST (signed, with retries)    │
  (Customer servers)            │                                         │
                                └─────────────────────────────────────────┘
```

### Key Design Decisions

1. **Single process.** API + Worker + Dashboard all run in one ASP.NET Core host process. No separate worker deployment for MVP. Simplifies deployment dramatically (`docker compose up` with 2 containers: app + postgres).

2. **PostgreSQL as queue.** Instead of requiring Redis or RabbitMQ, we use PostgreSQL `SKIP LOCKED` advisory locks for job queuing. This is proven at moderate scale (100-1000 deliveries/sec) and eliminates an entire infrastructure dependency.

3. **Bundled SPA.** React dashboard is built and served as static files from ASP.NET Core (`wwwroot`). No separate frontend deployment. One container = everything.

4. **Pluggable interfaces.** Queue backend, storage, and signing are behind interfaces. PostgreSQL queue can be swapped to Redis/RabbitMQ later without changing business logic.

---

## 2. Solution Structure

```
WebhookEngine/
├── src/
│   ├── WebhookEngine.Core/              # Domain models, interfaces, enums, metrics, options
│   │   ├── Entities/
│   │   │   ├── Application.cs
│   │   │   ├── DashboardUser.cs
│   │   │   ├── Endpoint.cs
│   │   │   ├── EndpointHealth.cs
│   │   │   ├── EventType.cs
│   │   │   ├── Message.cs
│   │   │   └── MessageAttempt.cs
│   │   ├── Enums/
│   │   │   ├── AttemptStatus.cs         # Success, Failed, Timeout, Sending
│   │   │   ├── CircuitState.cs          # Closed, Open, HalfOpen
│   │   │   ├── EndpointStatus.cs        # Active, Degraded, Failed, Disabled
│   │   │   └── MessageStatus.cs         # Pending, Sending, Delivered, Failed, DeadLetter
│   │   ├── Interfaces/
│   │   │   ├── IDeliveryNotifier.cs     # Abstraction for real-time notifications
│   │   │   ├── IDeliveryService.cs      # Abstraction for HTTP delivery
│   │   │   ├── IEndpointHealthTracker.cs
│   │   │   ├── IEndpointRateLimiter.cs
│   │   │   ├── IMessageQueue.cs         # Abstraction for job queuing
│   │   │   ├── IMessageStateMachine.cs  # Guards message status transitions
│   │   │   └── ISigningService.cs       # Abstraction for HMAC signing
│   │   ├── Metrics/
│   │   │   └── WebhookMetrics.cs        # Prometheus counters/histograms
│   │   ├── Models/
│   │   │   ├── DeliveryRequest.cs
│   │   │   ├── DeliveryResult.cs
│   │   │   └── SignedHeaders.cs
│   │   └── Options/
│   │       ├── CircuitBreakerOptions.cs
│   │       ├── DashboardAuthOptions.cs
│   │       ├── DeliveryOptions.cs
│   │       ├── RetentionOptions.cs
│   │       └── RetryPolicyOptions.cs
│   │
│   ├── WebhookEngine.Infrastructure/     # EF Core, PostgreSQL, implementations
│   │   ├── Data/
│   │   │   └── WebhookDbContext.cs
│   │   ├── Migrations/                  # EF Core migrations (auto-applied on startup)
│   │   ├── Queue/
│   │   │   └── PostgresMessageQueue.cs  # SKIP LOCKED based queue
│   │   ├── Services/
│   │   │   ├── EndpointHealthTracker.cs
│   │   │   ├── EndpointRateLimiter.cs
│   │   │   ├── HmacSigningService.cs
│   │   │   └── HttpDeliveryService.cs
│   │   └── Repositories/
│   │       ├── ApplicationRepository.cs
│   │       ├── DashboardStatsRepository.cs  # Single-query dashboard aggregation
│   │       ├── DashboardUserRepository.cs
│   │       ├── EndpointRepository.cs
│   │       ├── EventTypeRepository.cs
│   │       └── MessageRepository.cs
│   │
│   ├── WebhookEngine.Worker/            # Background delivery processing
│   │   ├── DeliveryWorker.cs            # IHostedService - polls queue, delivers
│   │   ├── RetryScheduler.cs            # Schedules retries based on backoff policy
│   │   ├── CircuitBreakerWorker.cs      # Monitors endpoint health, opens/closes circuits
│   │   ├── StaleLockRecoveryWorker.cs   # Recovers messages stuck in 'sending' > 5 min
│   │   └── RetentionCleanupWorker.cs    # Daily cleanup of expired messages (03:00 UTC)
│   │
│   ├── WebhookEngine.API/              # ASP.NET Core Web API host
│   │   ├── Auth/
│   │   │   └── PasswordHasher.cs
│   │   ├── Controllers/
│   │   │   ├── ApplicationsController.cs
│   │   │   ├── AuthController.cs              # Dashboard login/logout/me
│   │   │   ├── DashboardAnalyticsController.cs # Overview stats, timeline
│   │   │   ├── DashboardEndpointController.cs  # Dashboard endpoint management
│   │   │   ├── DashboardMessagesController.cs  # Dashboard message operations
│   │   │   ├── DevTrafficController.cs         # Dev traffic generator controls
│   │   │   ├── EndpointsController.cs
│   │   │   ├── EventTypesController.cs
│   │   │   ├── HealthController.cs
│   │   │   └── MessagesController.cs
│   │   ├── Hubs/
│   │   │   └── DeliveryHub.cs           # SignalR hub + SignalRDeliveryNotifier
│   │   ├── Middleware/
│   │   │   ├── ApiKeyAuthMiddleware.cs
│   │   │   ├── ExceptionHandlingMiddleware.cs
│   │   │   └── RequestLoggingMiddleware.cs
│   │   ├── Startup/
│   │   │   └── DashboardAdminSeeder.cs  # Seeds first admin from env vars
│   │   ├── Validators/
│   │   │   └── RequestValidators.cs     # FluentValidation rules
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   └── wwwroot/                     # React dashboard static files
│   │
│   └── WebhookEngine.Sdk/              # .NET SDK (NuGet package)
│       ├── WebhookEngineClient.cs
│       ├── Models/
│       └── WebhookEngine.Sdk.csproj
│
├── src/dashboard/                       # React SPA (Vite + TypeScript + Tailwind CSS)
│   ├── src/
│   │   ├── api/
│   │   │   ├── authApi.ts
│   │   │   └── dashboardApi.ts
│   │   ├── auth/
│   │   │   └── AuthContext.tsx           # React context for session auth
│   │   ├── components/
│   │   │   ├── ConfirmModal.tsx          # Confirmation dialog (themed)
│   │   │   ├── DeliveryTimeline.tsx      # Recharts time-series chart
│   │   │   ├── EndpointHealthBadge.tsx
│   │   │   ├── EventTypeSelect.tsx      # Multi-select chip/toggle
│   │   │   ├── Modal.tsx                # Base modal component
│   │   │   ├── PayloadViewer.tsx        # JSON viewer with syntax highlighting
│   │   │   ├── RetryButton.tsx
│   │   │   └── Select.tsx               # Custom dropdown (theme-consistent)
│   │   ├── hooks/
│   │   │   └── useDeliveryFeed.ts       # SignalR live feed
│   │   ├── layout/
│   │   │   └── AppShell.tsx             # Sidebar + main layout
│   │   ├── pages/
│   │   │   ├── ApplicationsPage.tsx
│   │   │   ├── DashboardPage.tsx        # Overview with charts
│   │   │   ├── DeliveryLogPage.tsx
│   │   │   ├── EndpointsPage.tsx
│   │   │   ├── LoginPage.tsx
│   │   │   └── MessagesPage.tsx
│   │   ├── routes/
│   │   │   └── ProtectedRoute.tsx       # Auth guard
│   │   ├── App.tsx
│   │   ├── main.tsx
│   │   ├── styles.css                   # Tailwind CSS v4 + custom tokens
│   │   └── types.ts
│   ├── package.json
│   └── vite.config.ts
│
├── tests/
│   ├── WebhookEngine.Core.Tests/
│   ├── WebhookEngine.Infrastructure.Tests/
│   ├── WebhookEngine.API.Tests/         # Integration tests
│   └── WebhookEngine.Worker.Tests/
│
├── docker/
│   ├── Dockerfile
│   ├── docker-compose.yml
│   └── docker-compose.dev.yml
│
├── docs/
│   ├── PRD.md
│   ├── ARCHITECTURE.md                  # (this file)
│   ├── DATABASE.md
│   ├── API.md
│   ├── GETTING-STARTED.md
│   ├── SELF-HOSTING.md
│   ├── RELEASE.md
│   ├── MVP-ROADMAP.md
│   ├── COMPETITIVE-ANALYSIS.md
│   ├── BUSINESS-MODEL.md
│   ├── adr/                             # Architecture Decision Records
│   ├── triage-flow.md
│   ├── backlog-v0.1.1.md
│   └── typescript-sdk-demand-criteria.md
│
├── README.md
├── LICENSE                              # MIT
├── .gitignore
└── WebhookEngine.sln

```

---

## 3. Core Components Detail

### 3.1 Delivery Pipeline (Happy Path)

```
1. Client calls POST /api/v1/applications/{appId}/messages
   Body: { "eventType": "order.created", "payload": { ... } }

2. API Controller:
   - Validates request
   - Resolves subscribed endpoints for this event type
   - For each endpoint, creates a Message record (status: Pending)
   - Enqueues each message to the PostgreSQL queue
   - Returns 202 Accepted with message IDs

3. Delivery Worker (Background Service):
   - Polls PostgreSQL queue using SELECT ... FOR UPDATE SKIP LOCKED
   - Dequeues message
   - Calls ISigningService to compute HMAC-SHA256 signature
   - Calls IDeliveryService to make HTTP POST to endpoint URL
   - Records MessageAttempt (status, response code, response body, latency)
   - If success (2xx): marks Message as Delivered
   - If failure: schedules retry based on RetryPolicy, marks Message as Pending with incremented attempt count
   - If max retries exceeded: marks Message as DeadLetter

4. Circuit Breaker Worker (Background Service):
   - Periodically checks recent delivery attempts per endpoint
   - If N consecutive failures: mark endpoint as Failed, stop delivering
   - If endpoint recovers (manual retry succeeds): reopen circuit
```

### 3.2 PostgreSQL-Based Queue

Instead of Redis/RabbitMQ, we use PostgreSQL itself as a job queue. This is a well-proven pattern used by Oban (Elixir), Que (Ruby), and others.

```sql
-- Queue polling query (Delivery Worker)
WITH next_job AS (
    SELECT id
    FROM messages
    WHERE status = 'pending'
      AND scheduled_at <= NOW()
    ORDER BY scheduled_at ASC
    LIMIT 10
    FOR UPDATE SKIP LOCKED
)
UPDATE messages
SET status = 'sending',
    locked_at = NOW(),
    locked_by = @workerId
FROM next_job
WHERE messages.id = next_job.id
RETURNING messages.*;
```

**Why this works for MVP:**
- Zero additional infrastructure
- ACID guarantees — no lost messages
- `SKIP LOCKED` prevents worker contention
- PostgreSQL handles 100-1000 jobs/second easily
- Can be swapped to Redis/RabbitMQ via `IMessageQueue` interface when scaling

**When to upgrade:**
- If sustained throughput > 1000 deliveries/second
- If queue depth regularly > 100K messages
- If delivery latency requirements drop below 100ms

### 3.3 HMAC Signing

Following the [Standard Webhooks](https://www.standardwebhooks.com/) specification:

```csharp
public class HmacSigningService : ISigningService
{
    public SignedHeaders Sign(string messageId, long timestamp, string body, string secret)
    {
        var payload = $"{messageId}.{timestamp}.{body}";
        var secretBytes = Convert.FromBase64String(secret);

        using var hmac = new HMACSHA256(secretBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToBase64String(hash);

        return new SignedHeaders
        {
            WebhookId = messageId,
            WebhookTimestamp = timestamp.ToString(),
            WebhookSignature = $"v1,{signature}"
        };
    }
}
```

### 3.4 Circuit Breaker

Per-endpoint circuit breaker prevents hammering a dead endpoint:

```
State Machine:
  CLOSED (normal) → OPEN (after N consecutive failures) → HALF_OPEN (after cooldown) → CLOSED or OPEN

Parameters:
  - failureThreshold: 5 consecutive failures → open circuit
  - cooldownPeriod: 5 minutes before retrying (HALF_OPEN)
  - successThreshold: 1 success in HALF_OPEN → close circuit

When circuit is OPEN:
  - No deliveries attempted
  - Messages queue up
  - Endpoint status shows "Failed" in dashboard
  - Alert via dashboard notification

When circuit transitions to HALF_OPEN:
  - One test delivery is attempted
  - If success → CLOSED, flush queued messages
  - If failure → OPEN, restart cooldown
```

### 3.5 HTTP Delivery Service

```csharp
public class HttpDeliveryService : IDeliveryService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public async Task<DeliveryResult> DeliverAsync(DeliveryRequest request, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("webhook-delivery");
        // Timeout configured via DeliveryOptions (default 30s)

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.EndpointUrl);
        httpRequest.Content = new StringContent(request.Payload, Encoding.UTF8, "application/json");

        // Add signature headers
        httpRequest.Headers.Add("webhook-id", request.SignedHeaders.WebhookId);
        httpRequest.Headers.Add("webhook-timestamp", request.SignedHeaders.WebhookTimestamp);
        httpRequest.Headers.Add("webhook-signature", request.SignedHeaders.WebhookSignature);
        httpRequest.Headers.Add("User-Agent", "WebhookEngine/1.0");

        // Add custom endpoint headers
        foreach (var header in request.CustomHeaders)
            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await client.SendAsync(httpRequest, ct);
            stopwatch.Stop();

            return new DeliveryResult
            {
                Success = response.IsSuccessStatusCode,
                StatusCode = (int)response.StatusCode,
                ResponseBody = await response.Content.ReadAsStringAsync(ct),
                LatencyMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (TaskCanceledException) // timeout
        {
            return new DeliveryResult { Success = false, StatusCode = 0, Error = "Timeout", LatencyMs = stopwatch.ElapsedMilliseconds };
        }
        catch (HttpRequestException ex) // connection refused, DNS failure, etc.
        {
            return new DeliveryResult { Success = false, StatusCode = 0, Error = ex.Message, LatencyMs = stopwatch.ElapsedMilliseconds };
        }
    }
}
```

---

## 4. Authentication & Authorization

### 4.1 API Authentication
Two levels:
1. **Application API Key** — each application gets a unique API key. Used by SaaS backends to send messages. Sent as `Authorization: Bearer {apiKey}`.
2. **Dashboard Auth** — built-in cookie auth (email/password) for dashboard access. Optional: OAuth (GitHub/Google) post-MVP.

### 4.2 API Key Design
```
Format: whe_{appId_short}_{random_32chars}
Example: whe_app1a2b3_xK9mNpQrStUvWxYz1234567890abcdef

Stored: SHA256 hash in database (never stored in plaintext)
Lookup: prefix (whe_app1a2b3_) used for fast lookup, hash compared for verification
```

---

## 5. Scalability Path

### MVP (Month 1-3)
```
Single instance, PostgreSQL queue
Throughput: ~100-500 deliveries/sec
Good for: up to ~50M deliveries/month
```

### Scale Phase 1 (Month 4-6)
```
Multiple worker instances (same DB)
SKIP LOCKED ensures no duplicate processing
Throughput: ~500-2000 deliveries/sec
Add: Redis for caching, rate limiting
```

### Scale Phase 2 (Month 6-12)
```
Dedicated queue (RabbitMQ or Redis Streams)
Separate API and Worker deployments
Read replicas for dashboard queries
Throughput: ~5000+ deliveries/sec
```

---

## 6. Deployment Architecture

### Docker Compose (MVP)

```yaml
# docker-compose.yml
services:
  webhook-engine:
    image: voyvodka/webhook-engine:latest
    ports:
      - "5100:8080"
    environment:
      - ConnectionStrings__Default=Host=postgres;Database=webhookengine;...
      - WebhookEngine__DashboardAuth__AdminEmail=admin@example.com
      - WebhookEngine__DashboardAuth__AdminPassword=changeme
    depends_on:
      - postgres

  postgres:
    image: postgres:17
    volumes:
      - pgdata:/var/lib/postgresql/data
    environment:
      - POSTGRES_DB=webhookengine
      - POSTGRES_USER=webhookengine
      - POSTGRES_PASSWORD=webhookengine

volumes:
  pgdata:
```

**That's it.** `docker compose up` and you have a fully functional webhook delivery platform.

---

## 7. Technology Choices & Rationale

| Decision | Choice | Why |
|----------|--------|-----|
| Language | C# / .NET 10 | Strongest skill. Enterprise ecosystem. Excellent async/parallel support. |
| Web Framework | ASP.NET Core Controllers | Best .NET web framework. Fast, mature, well-documented. |
| ORM | Entity Framework Core | Productivity for CRUD. Migrations built-in. |
| Database | PostgreSQL | Free, powerful, JSON support, SKIP LOCKED for queue, industry standard. |
| Queue | PostgreSQL SKIP LOCKED | Zero extra dependencies for MVP. Proven pattern. |
| Frontend | React + TypeScript + Vite + Tailwind CSS 4 + Recharts + Lucide React | Most popular SPA framework. Tailwind for utility-first styling. |
| Real-time | SignalR | Native ASP.NET Core integration. Dashboard live updates. |
| HTTP Client | IHttpClientFactory | Built-in .NET. Handles DNS caching, connection pooling. |
| Logging | Serilog | Structured logging standard in .NET. JSON output for Docker. |
| Testing | xUnit + FluentAssertions + Testcontainers | Industry standard .NET testing. Testcontainers for integration tests with real PostgreSQL. |
| Container | Docker + Docker Compose | Universal deployment standard. |
| License | MIT | Maximum adoption. No restrictions. |
