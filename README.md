# WebhookEngine

Self-hosted webhook delivery platform with reliable at-least-once delivery, exponential backoff retries, per-endpoint circuit breakers, and a real-time dashboard.

## Features

- **Reliable delivery** -- PostgreSQL-backed queue with `SELECT ... FOR UPDATE SKIP LOCKED`, no Redis/RabbitMQ needed
- **At-least-once semantics** -- messages are never lost; stale lock recovery handles worker crashes
- **Exponential backoff** -- 7 retry attempts (5s, 30s, 2m, 15m, 1h, 6h, 24h)
- **Circuit breaker** -- per-endpoint, auto-opens after 5 consecutive failures, 5-minute cooldown
- **HMAC-SHA256 signing** -- Standard Webhooks spec (`webhook-id`, `webhook-timestamp`, `webhook-signature`)
- **Idempotency** -- optional `idempotencyKey` prevents duplicate deliveries
- **Real-time dashboard** -- React SPA with live delivery feed via SignalR
- **Single process** -- API + background workers + dashboard served from one ASP.NET Core host
- **Data retention** -- automatic cleanup (delivered: 30 days, dead-letter: 90 days)

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | C# / .NET 10, ASP.NET Core, Entity Framework Core |
| Database | PostgreSQL 17+ |
| Frontend | React 19, TypeScript 5.9, Vite 7, Tailwind CSS 4, Recharts 3, Lucide React |
| Real-time | SignalR |
| Testing | xUnit, FluentAssertions, NSubstitute, Testcontainers |
| Logging | Serilog (structured JSON) |
| Validation | FluentValidation |
| Observability | OpenTelemetry + Prometheus metrics exporter |
| Deployment | Docker Compose |

## Quick Start

### Docker Compose (recommended)

```bash
git clone https://github.com/voyvodka/webhook-engine.git
cd webhook-engine
docker compose -f docker/docker-compose.yml up -d
```

The app starts on `http://localhost:5100`. Dashboard login: `admin@example.com` / `changeme`.

### Local Development

**Prerequisites:** .NET 10 SDK, PostgreSQL 17+, Node.js 20+, Yarn

1. **Start PostgreSQL** (or use the dev compose file):

```bash
docker compose -f docker/docker-compose.dev.yml up -d
```

2. **Configure connection string** in `src/WebhookEngine.API/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=webhookengine;Username=webhookengine;Password=webhookengine"
  }
}
```

3. **Run the backend** (migrations auto-apply on startup):

```bash
dotnet run --project src/WebhookEngine.API
```

The API starts on `http://localhost:5128`.

4. **Run the dashboard** (optional, for frontend development):

```bash
cd src/dashboard
yarn install
yarn dev
```

Dashboard dev server runs on `http://localhost:5173` with API proxy to `localhost:5128`.

## Documentation

- [Getting Started](docs/GETTING-STARTED.md) — from zero to first webhook
- [Self-Hosting Guide](docs/SELF-HOSTING.md) — production deployment and operations
- [Release Guide](docs/RELEASE.md) — Docker Hub and NuGet publishing flow
- [Launch Checklist](docs/LAUNCH-CHECKLIST.md) — final pre-launch and go-live tracking
- [Roadmap](docs/ROADMAP.md) — current phase status and upcoming priorities
- [PRD](docs/PRD.md) — product scope, goals, and requirement definitions
- [API Reference](docs/API.md) — full endpoint documentation
- [Architecture](docs/ARCHITECTURE.md) — system design and component overview
- [Database](docs/DATABASE.md) — schema and PostgreSQL notes
- [Contributing](CONTRIBUTING.md) — local setup and pull request workflow
- [Changelog](CHANGELOG.md) — notable project changes
- [Samples guide](samples/README.md), [Sample Sender](samples/WebhookEngine.Sample.Sender/Program.cs), and [Sample Receiver](samples/WebhookEngine.Sample.Receiver/Program.cs)
- [Signature verification helpers](samples/signature-verification/) for C#, TypeScript, and Python

## Architecture

```
                    +---------------------------+
                    |    ASP.NET Core Host       |
                    |                           |
   HTTP requests -> | Controllers (REST API)    |
                    | Middleware (auth, logging) |
                    | Static files (React SPA)  |
                    | SignalR Hub               |
                    |                           |
                    | Background Workers:       |
                    |  - DeliveryWorker         |
                    |  - RetryScheduler         |
                    |  - CircuitBreakerWorker   |
                    |  - StaleLockRecovery      |
                    |  - RetentionCleanup       |
                    +------------+--------------+
                                 |
                                 v
                    +---------------------------+
                    |     PostgreSQL 17+         |
                    |  - Data storage            |
                    |  - Job queue (SKIP LOCKED) |
                    +---------------------------+
```

### Solution Structure

```
src/
  WebhookEngine.Core/            # Domain: entities, enums, interfaces, options
  WebhookEngine.Infrastructure/   # EF Core, PostgreSQL queue, repositories, services
  WebhookEngine.Application/      # DI registration (CQRS scaffold, not yet implemented)
  WebhookEngine.Worker/           # Background services (delivery, retry, circuit breaker)
  WebhookEngine.API/              # ASP.NET Core host, controllers, middleware
  WebhookEngine.Sdk/              # .NET client SDK
  dashboard/                      # React SPA (Vite + TypeScript)
tests/
  WebhookEngine.Core.Tests/
  WebhookEngine.Infrastructure.Tests/
  WebhookEngine.Application.Tests/
  WebhookEngine.API.Tests/
  WebhookEngine.Worker.Tests/
```

## API Overview

Base URL: `/api/v1/`

### Authentication

- **API key** (for programmatic access): `Authorization: Bearer whe_{appId}_{random}`
- **Cookie auth** (for dashboard): `POST /api/v1/auth/login`

### Endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/health` or `/api/v1/health` | None | Health check |
| `POST` | `/api/v1/auth/login` | None | Dashboard login |
| `POST` | `/api/v1/auth/logout` | Cookie | Dashboard logout |
| `GET` | `/api/v1/auth/me` | Cookie | Current user |
| `GET` | `/api/v1/applications` | Cookie | List applications |
| `POST` | `/api/v1/applications` | Cookie | Create application |
| `GET` | `/api/v1/applications/{id}` | Cookie | Get application |
| `PUT` | `/api/v1/applications/{id}` | Cookie | Update application |
| `DELETE` | `/api/v1/applications/{id}` | Cookie | Delete application |
| `POST` | `/api/v1/applications/{id}/rotate-key` | Cookie | Rotate API key |
| `GET` | `/api/v1/event-types` | API key | List event types |
| `POST` | `/api/v1/event-types` | API key | Create event type |
| `GET` | `/api/v1/endpoints` | API key | List endpoints |
| `POST` | `/api/v1/endpoints` | API key | Create endpoint |
| `PUT` | `/api/v1/endpoints/{id}` | API key | Update endpoint |
| `DELETE` | `/api/v1/endpoints/{id}` | API key | Delete endpoint |
| `POST` | `/api/v1/endpoints/{id}/disable` | API key | Disable endpoint |
| `POST` | `/api/v1/endpoints/{id}/enable` | API key | Enable endpoint |
| `POST` | `/api/v1/messages` | API key | Send message |
| `POST` | `/api/v1/messages/batch` | API key | Batch send messages |
| `POST` | `/api/v1/messages/replay` | API key | Replay historical messages |
| `GET` | `/api/v1/messages` | API key | List messages |
| `GET` | `/api/v1/messages/{id}` | API key | Get message |
| `GET` | `/api/v1/messages/{id}/attempts` | API key | List attempts |
| `POST` | `/api/v1/messages/{id}/retry` | API key | Retry message |
| `GET` | `/api/v1/dashboard/overview` | Cookie | Dashboard stats |
| `GET` | `/api/v1/dashboard/timeline` | Cookie | Delivery chart data |
| `GET` | `/api/v1/dashboard/event-types` | Cookie | List event types (cross-app) |
| `POST` | `/api/v1/dashboard/event-types` | Cookie | Create event type |
| `PUT` | `/api/v1/dashboard/event-types/{id}` | Cookie | Update event type |
| `DELETE` | `/api/v1/dashboard/event-types/{id}` | Cookie | Archive event type |

### Send a Message

```bash
curl -X POST http://localhost:5128/api/v1/messages \
  -H "Authorization: Bearer whe_abc123_your-api-key" \
  -H "Content-Type: application/json" \
  -d '{
    "eventType": "order.created",
    "payload": {"orderId": 42, "amount": 99.99},
    "idempotencyKey": "order-42"
  }'
```

Response:

```json
{
  "data": {
    "messageIds": ["msg_abc123..."],
    "endpointCount": 2,
    "eventType": "order.created"
  },
  "meta": { "requestId": "req_..." }
}
```

### .NET SDK

```csharp
using WebhookEngine.Sdk;

using var client = new WebhookEngineClient("whe_abc_your-api-key", "http://localhost:5128");

await client.Messages.SendAsync(new SendMessageRequest
{
    EventType = "order.created",
    Payload = new { orderId = 42, amount = 99.99 },
    IdempotencyKey = "order-42"
});
```

## Webhook Signature Verification

WebhookEngine signs every delivery with HMAC-SHA256 following the [Standard Webhooks](https://www.standardwebhooks.com/) spec. Receivers should verify signatures like this:

```python
# Python example
import hmac, hashlib, base64

def verify_webhook(body: bytes, secret: str, headers: dict) -> bool:
    msg_id = headers["webhook-id"]
    timestamp = headers["webhook-timestamp"]
    signature = headers["webhook-signature"]

    payload = f"{msg_id}.{timestamp}.{body.decode()}"
    secret_bytes = base64.b64decode(secret)
    expected = hmac.new(secret_bytes, payload.encode(), hashlib.sha256).digest()
    expected_sig = f"v1,{base64.b64encode(expected).decode()}"

    return hmac.compare_digest(signature, expected_sig)
```

## Configuration

All configuration is via `appsettings.json` or environment variables (double-underscore notation):

| Setting | Default | Description |
|---|---|---|
| `ConnectionStrings__Default` | -- | PostgreSQL connection string |
| `WebhookEngine__Delivery__TimeoutSeconds` | `30` | HTTP delivery timeout |
| `WebhookEngine__Delivery__BatchSize` | `10` | Messages dequeued per batch |
| `WebhookEngine__Delivery__PollIntervalMs` | `1000` | Queue poll interval (empty queue) |
| `WebhookEngine__Delivery__StaleLockMinutes` | `5` | Stale lock recovery threshold |
| `WebhookEngine__RetryPolicy__MaxRetries` | `7` | Max delivery attempts |
| `WebhookEngine__RetryPolicy__BackoffSchedule` | `[5,30,120,900,3600,21600,86400]` | Backoff in seconds |
| `WebhookEngine__CircuitBreaker__FailureThreshold` | `5` | Failures to open circuit |
| `WebhookEngine__CircuitBreaker__CooldownMinutes` | `5` | Cooldown before half-open |
| `WebhookEngine__DashboardAuth__AdminEmail` | `admin@example.com` | Initial admin email |
| `WebhookEngine__DashboardAuth__AdminPassword` | `changeme` | Initial admin password |
| `WebhookEngine__Retention__DeliveredRetentionDays` | `30` | Days to keep delivered messages |
| `WebhookEngine__Retention__DeadLetterRetentionDays` | `90` | Days to keep dead-letter messages |

## Build & Test

```bash
# Build
dotnet build WebhookEngine.sln

# Run all tests (106 tests)
dotnet test WebhookEngine.sln

# Run specific test project
dotnet test tests/WebhookEngine.Core.Tests

# Run tests matching a pattern
dotnet test --filter "DisplayName~HmacSigning"

# Dashboard build
cd src/dashboard && yarn install && yarn build
```

## Docker

```bash
# Production
docker compose -f docker/docker-compose.yml up -d

# Stop production services
docker compose -f docker/docker-compose.yml down

# Reset production data (removes PostgreSQL volume)
docker compose -f docker/docker-compose.yml down -v

# Development (starts PostgreSQL only, run backend separately)
docker compose -f docker/docker-compose.dev.yml up -d
dotnet run --project src/WebhookEngine.API
```

`docker/docker-compose.yml` uses a persistent PostgreSQL volume (`pgdata`), so old applications/endpoints remain after restart unless you run `down -v`.

## Prometheus Metrics

WebhookEngine exposes Prometheus metrics at `GET /metrics`. No authentication required.

```bash
curl http://localhost:5128/metrics
```

### Custom Metrics

| Metric | Type | Description |
|---|---|---|
| `webhookengine_messages_enqueued` | Counter | Total messages enqueued |
| `webhookengine_deliveries_total` | Counter | Total delivery attempts |
| `webhookengine_deliveries_success` | Counter | Successful deliveries |
| `webhookengine_deliveries_failed` | Counter | Failed deliveries |
| `webhookengine_deadletter_total` | Counter | Messages moved to dead letter |
| `webhookengine_retries_scheduled` | Counter | Retry attempts scheduled |
| `webhookengine_circuit_opened` | Counter | Circuit breaker open events |
| `webhookengine_circuit_closed` | Counter | Circuit breaker close events |
| `webhookengine_stalelock_recovered` | Counter | Stale locks recovered |
| `webhookengine_delivery_duration` | Histogram | Delivery duration (ms) |
| `webhookengine_queue_depth` | UpDownCounter | Approximate queue depth |

### Built-in Metrics

ASP.NET Core request metrics and .NET runtime metrics (GC, thread pool, etc.) are also included automatically.

### Prometheus Scrape Config

```yaml
# prometheus.yml
scrape_configs:
  - job_name: webhookengine
    scrape_interval: 15s
    static_configs:
      - targets: ["localhost:5128"]
```

## Message Lifecycle

```
  Send API call
       |
       v
  [Pending] --dequeue--> [Sending] --success--> [Delivered]
       ^                     |
       |                     | failure
       |                     v
       +--retry-schedule-- [Failed] --max-retries--> [DeadLetter]
```

- **Pending**: Queued for delivery
- **Sending**: Locked by a worker, in-flight
- **Delivered**: Successfully delivered (HTTP 2xx)
- **Failed**: Delivery failed, scheduled for retry
- **DeadLetter**: All retry attempts exhausted

## License

MIT
