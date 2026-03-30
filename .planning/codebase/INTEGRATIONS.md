# External Integrations

**Analysis Date:** 2026-03-30

## APIs & External Services

**Webhook Delivery:**
- **Target Endpoints** - Customer-provided HTTP(S) endpoints for webhook delivery
  - SDK/Client: `System.Net.Http.HttpClient` (via `Microsoft.Extensions.Http`)
  - Implementation: `src/WebhookEngine.Infrastructure/Services/HttpDeliveryService.cs`
  - Method: HTTP POST with JSON payload + HMAC signature headers
  - Timeout: Configurable via `WebhookEngine:Delivery:TimeoutSeconds` (default 30s)
  - Custom Headers: Per-endpoint custom headers stored in `endpoints.custom_headers` (jsonb)
  - Signature Headers: `webhook-id`, `webhook-timestamp`, `webhook-signature` (HMAC-SHA256)

## Data Storage

**Databases:**
- **PostgreSQL 17** (production) / 12+ (development)
  - Connection: `ConnectionStrings:Default` (via `src/WebhookEngine.API/appsettings.json`)
  - Client: Entity Framework Core 10.0.3 with Npgsql provider
  - Database name: `webhookengine`
  - Default user: `webhookengine` (password via `POSTGRES_PASSWORD` env var)
  - Schema: Managed by EF Core migrations (`src/WebhookEngine.Infrastructure/Migrations/`)
  - Tables:
    - `applications` - Webhook applications/customers
    - `event_types` - Event categories per application
    - `endpoints` - Webhook receiver URLs
    - `messages` - Webhook payloads to deliver
    - `message_attempts` - Delivery attempt history
    - `endpoint_health` - Circuit breaker state
    - `dashboard_users` - Dashboard login accounts
  - Special: JSONB columns for flexible schema (`retry_policy`, `schema_json`, `custom_headers`, `metadata`, `payload`)
  - Queue mechanism: `messages` table with status/locked_at indexes for polling-based queue (`src/WebhookEngine.Infrastructure/Queue/PostgresMessageQueue.cs`)

**File Storage:**
- Not used - all data stored in PostgreSQL

**Caching:**
- Not used - no Redis or distributed cache

## Authentication & Identity

**Auth Provider:**
- **Custom Cookie-based Authentication**
  - Implementation: ASP.NET Core built-in cookie auth
  - Cookie name: `webhookengine_dashboard`
  - Expiration: 7 days sliding
  - HttpOnly: true, SameSite: Lax
  - Repository: `src/WebhookEngine.Infrastructure/Repositories/DashboardUserRepository.cs`
  - Database table: `dashboard_users` (email, password_hash, role, created_at, last_login_at)
  - Admin seeding: `src/WebhookEngine.API/Startup/DashboardAdminSeeder.cs`
  - Admin credentials from: `WebhookEngine:DashboardAuth:AdminEmail` and `WebhookEngine:DashboardAuth:AdminPassword`

**API Key Authentication:**
- **Custom API Key scheme** for webhook operations (not dashboard)
  - Implementation: Middleware `src/WebhookEngine.API/Middleware/ApiKeyAuthMiddleware.cs`
  - Header name: `X-API-Key` (inferred from middleware pattern)
  - Key storage: `applications.api_key_hash` (SHA256) + `applications.api_key_prefix` (for lookup)
  - Purpose: Authenticates incoming webhook registration/management requests

## Monitoring & Observability

**Error Tracking:**
- Not integrated - errors logged via Serilog only

**Logs:**
- **Serilog with JSON formatter to stdout**
  - Configuration: `src/WebhookEngine.API/appsettings.json` `Serilog` section
  - Output: JSON-formatted console logs (suitable for container log aggregation)
  - Minimum level: Information (warnings from Microsoft.EntityFrameworkCore)
  - Middleware: `src/WebhookEngine.API/Middleware/RequestLoggingMiddleware.cs` (logs all requests)
  - Exception handler: `src/WebhookEngine.API/Middleware/ExceptionHandlingMiddleware.cs`

**Metrics:**
- **OpenTelemetry + Prometheus**
  - Exporter: `OpenTelemetry.Exporter.Prometheus.AspNetCore` 1.15.0-beta.1
  - Endpoint: `GET /metrics` (Prometheus scraping format)
  - Instrumentation:
    - ASP.NET Core (`OpenTelemetry.Instrumentation.AspNetCore`) - HTTP request metrics
    - Runtime (`OpenTelemetry.Instrumentation.Runtime`) - GC, memory, thread metrics
  - Custom metrics: `src/WebhookEngine.Core/Metrics/WebhookMetrics.cs`
    - Meter name: `WebhookEngine.Metrics` (inferred)
    - Tracked: Message enqueued, delivery success/failure, queue size, retry events
  - Scrape interval: Default Prometheus scrape configuration

**Health Check:**
- Docker HEALTHCHECK: `wget --spider http://localhost:8080/health` (30s interval, 5s timeout)

## CI/CD & Deployment

**Hosting:**
- **Docker Compose** (primary deployment method)
  - Images:
    - `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` - Application runtime
    - `postgres:17-alpine` - PostgreSQL database
  - Orchestration: `docker/docker-compose.yml` (production) + `docker/docker-compose.dev.yml` (development)
  - Build context: Root directory with Dockerfile at `docker/Dockerfile`

**CI Pipeline:**
- GitHub Actions workflows in `.github/workflows/` (not analyzed in detail)
- Docker build stages:
  1. Node.js 20 Alpine - Dashboard build (`yarn build`)
  2. .NET SDK 10.0 - Backend restore and publish
  3. .NET Runtime 10.0 Alpine - Minimal runtime image

## Environment Configuration

**Required env vars (from docker-compose.yml):**
- `ConnectionStrings__Default` - PostgreSQL connection string (e.g., `Host=postgres;Port=5432;Database=webhookengine;Username=webhookengine;Password={POSTGRES_PASSWORD}`)
- `POSTGRES_PASSWORD` - Database password (default: `webhookengine`)
- `WebhookEngine__DashboardAuth__AdminEmail` - Dashboard admin email (default: `admin@example.com`)
- `WebhookEngine__DashboardAuth__AdminPassword` - Dashboard admin password (default: `changeme`)
- `APP_PORT` - Exposed port mapping (default: `5100`)

**Configuration sources (in order):**
1. `appsettings.json` (defaults)
2. `appsettings.{ASPNETCORE_ENVIRONMENT}.json` (overrides)
3. Environment variables (via ASP.NET Core configuration system)

**Secrets location:**
- Environment variables (Docker Compose `.env` file, or passed at runtime)
- No `.env` file tracked; `.env.example` at `docker/.env.example`
- Production: Use Docker Compose secrets or container orchestration secrets

## Webhooks & Callbacks

**Incoming:**
- REST API endpoints at `/api/v1/*` for:
  - Application management: Create, update, list applications
  - Endpoint management: Create, update, delete webhook endpoints
  - Event type management: Define event schemas
  - Message management: Trigger webhook messages, query delivery status
  - Authentication: `/api/v1/auth/login`, `/api/v1/auth/logout`, `/api/v1/auth/me`
  - Dashboard: `/api/v1/dashboard/*` for analytics and monitoring
- SignalR WebSocket hub at `/hubs/deliveries` for real-time delivery updates

**Outgoing:**
- HTTP POST to customer-provided webhook endpoints
- Payload: JSON with HMAC-SHA256 signature
- Headers:
  - `webhook-id` - Unique message identifier
  - `webhook-timestamp` - ISO 8601 timestamp
  - `webhook-signature` - HMAC signature for payload verification
  - `User-Agent` - `WebhookEngine/1.0`
  - Custom headers: Per-endpoint custom headers

## Real-time Updates

**SignalR Hub:**
- Hub location: `src/WebhookEngine.API/Hubs/DeliveryHub.cs`
- Connection: WebSocket at `/hubs/deliveries`
- Client: `@microsoft/signalr` 10.0.0 in dashboard
- Implementation: `SignalRDeliveryNotifier` service pushes delivery status changes to all connected clients
- Use case: Real-time delivery status updates in dashboard without polling

---

*Integration audit: 2026-03-30*
