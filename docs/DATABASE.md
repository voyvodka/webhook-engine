# Database Schema
# WebhookEngine

**Database:** PostgreSQL 17+
**ORM:** Entity Framework Core
**Schema:** `public` (default)

---

## 1. Entity Relationship Diagram

```
┌───────────────┐       ┌───────────────┐       ┌───────────────┐
│  applications │       │  event_types  │       │   endpoints   │
├───────────────┤       ├───────────────┤       ├───────────────┤
│ id (PK)       │──┐    │ id (PK)       │    ┌──│ id (PK)       │
│ name          │  │    │ app_id (FK)   │──┐ │  │ app_id (FK)   │──┐
│ api_key_hash  │  │    │ name          │  │ │  │ url           │  │
│ signing_secret│  │    │ description   │  │ │  │ description   │  │
│ retry_policy  │  │    │ schema_json   │  │ │  │ status        │  │
│ created_at    │  │    │ is_archived   │  │ │  │ custom_headers│  │
│ updated_at    │  │    │ created_at    │  │ │  │ secret_override│ │
└───────────────┘  │    └───────────────┘  │ │  │ metadata      │  │
                   │                       │ │  │ created_at    │  │
                   │                       │ │  │ updated_at    │  │
                   │    ┌──────────────────┐│ │  └───────────────┘  │
                   │    │endpoint_event_   ││ │                     │
                   │    │   types         ││ │                     │
                   │    ├──────────────────┤│ │                     │
                   │    │ endpoint_id (FK) │┘ │                     │
                   │    │ event_type_id(FK)│──┘                     │
                   │    └──────────────────┘                        │
                   │                                                │
                   │    ┌───────────────┐       ┌──────────────────┐│
                   │    │   messages    │       │ message_attempts ││
                   │    ├───────────────┤       ├──────────────────┤│
                   │    │ id (PK)       │──┐    │ id (PK)         ││
                   └───►│ app_id (FK)   │  │    │ message_id (FK) │┘
                        │ endpoint_id   │──┘───►│ endpoint_id(FK) │
                        │ event_type_id │       │ attempt_number  │
                        │ payload       │       │ status          │
                        │ status        │       │ status_code     │
                        │ idempotency_k │       │ response_body   │
                        │ attempt_count │       │ latency_ms      │
                        │ max_retries   │       │ error           │
                        │ scheduled_at  │       │ created_at      │
                        │ locked_at     │       └──────────────────┘
                        │ locked_by     │
                        │ delivered_at  │
                        │ created_at    │
                        └───────────────┘

┌──────────────────┐
│  endpoint_health │
├──────────────────┤
│ endpoint_id (PK) │
│ circuit_state    │
│ consecutive_fails│
│ last_failure_at  │
│ last_success_at  │
│ cooldown_until   │
│ updated_at       │
└──────────────────┘

┌──────────────────┐
│  dashboard_users │
├──────────────────┤
│ id (PK)          │
│ email            │
│ password_hash    │
│ role             │
│ created_at       │
│ last_login_at    │
└──────────────────┘
```

---

## 2. Table Definitions

### 2.1 applications

Logical tenants within WebhookEngine. A SaaS company using WebhookEngine might create one application per product, or one application per environment (staging/production).

```sql
CREATE TABLE applications (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name            VARCHAR(255) NOT NULL,
    api_key_prefix  VARCHAR(20) NOT NULL UNIQUE,   -- "whe_app1a2b3_" for fast lookup
    api_key_hash    VARCHAR(64) NOT NULL,           -- SHA256 hash of full API key
    signing_secret  VARCHAR(64) NOT NULL,           -- Base64-encoded HMAC secret
    retry_policy    JSONB NOT NULL DEFAULT '{"maxRetries":7,"backoffSchedule":[5,30,120,900,3600,21600,86400]}',
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_applications_api_key_prefix ON applications (api_key_prefix);
```

### 2.2 event_types

Categorizes the events an application can send (e.g., `order.created`, `payment.failed`).

```sql
CREATE TABLE event_types (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_id          UUID NOT NULL REFERENCES applications(id) ON DELETE CASCADE,
    name            VARCHAR(255) NOT NULL,          -- "order.created"
    description     TEXT,
    schema_json     JSONB,                          -- Optional JSON Schema for validation
    is_archived     BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    UNIQUE (app_id, name)
);

CREATE INDEX idx_event_types_app_id ON event_types (app_id);
```

### 2.3 endpoints

Webhook endpoints registered by API consumers. Each endpoint subscribes to specific event types.

```sql
CREATE TABLE endpoints (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_id          UUID NOT NULL REFERENCES applications(id) ON DELETE CASCADE,
    url             VARCHAR(2048) NOT NULL,
    description     VARCHAR(500),
    status          VARCHAR(20) NOT NULL DEFAULT 'active',  -- active, disabled
    custom_headers  JSONB DEFAULT '{}',             -- {"Authorization": "Bearer xyz"}
    secret_override VARCHAR(64),                    -- Override app-level signing secret
    metadata        JSONB DEFAULT '{}',             -- Arbitrary key-value pairs
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_endpoints_app_id ON endpoints (app_id);
CREATE INDEX idx_endpoints_status ON endpoints (app_id, status);
```

### 2.4 endpoint_event_types

Many-to-many: which endpoints subscribe to which event types. If an endpoint has zero subscriptions, it receives ALL event types for that application.

```sql
CREATE TABLE endpoint_event_types (
    endpoint_id     UUID NOT NULL REFERENCES endpoints(id) ON DELETE CASCADE,
    event_type_id   UUID NOT NULL REFERENCES event_types(id) ON DELETE CASCADE,

    PRIMARY KEY (endpoint_id, event_type_id)
);
```

### 2.5 messages

The core table. Each row represents one webhook message to be delivered to one endpoint. When a message is sent via API, one row is created per subscribed endpoint (fan-out).

Also serves as the **job queue** — the Delivery Worker polls this table.

```sql
CREATE TABLE messages (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_id          UUID NOT NULL REFERENCES applications(id),
    endpoint_id     UUID NOT NULL REFERENCES endpoints(id),
    event_type_id   UUID NOT NULL REFERENCES event_types(id),
    event_id        VARCHAR(64),                    -- Client-provided event identifier
    idempotency_key VARCHAR(128),                   -- Prevents duplicate sends
    payload         JSONB NOT NULL,
    status          VARCHAR(20) NOT NULL DEFAULT 'pending',
                    -- pending, sending, delivered, failed, dead_letter
    attempt_count   INT NOT NULL DEFAULT 0,
    max_retries     INT NOT NULL DEFAULT 7,
    scheduled_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),  -- When to attempt next delivery
    locked_at       TIMESTAMPTZ,                    -- Queue lock timestamp
    locked_by       VARCHAR(64),                    -- Worker instance ID
    delivered_at    TIMESTAMPTZ,                    -- When successfully delivered
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    UNIQUE (app_id, idempotency_key)                -- Idempotency per application
);

-- Queue polling index (critical for performance)
CREATE INDEX idx_messages_queue ON messages (scheduled_at ASC)
    WHERE status = 'pending';

-- Filtering indexes
CREATE INDEX idx_messages_app_endpoint ON messages (app_id, endpoint_id);
CREATE INDEX idx_messages_app_status ON messages (app_id, status);
CREATE INDEX idx_messages_app_event_type ON messages (app_id, event_type_id);
CREATE INDEX idx_messages_created_at ON messages (app_id, created_at DESC);

-- Stale lock cleanup (find messages locked > 5 minutes ago — worker crashed)
CREATE INDEX idx_messages_stale_locks ON messages (locked_at)
    WHERE status = 'sending' AND locked_at IS NOT NULL;
```

### 2.6 message_attempts

Immutable log of every delivery attempt. One message can have multiple attempts (retries).

```sql
CREATE TABLE message_attempts (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    message_id      UUID NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    endpoint_id     UUID NOT NULL REFERENCES endpoints(id),
    attempt_number  INT NOT NULL,                   -- 1, 2, 3, ...
    status          VARCHAR(20) NOT NULL,           -- success, failed, timeout
    status_code     INT,                            -- HTTP status code (null if connection failed)
    request_headers JSONB,                          -- What we sent
    response_body   TEXT,                           -- What we got back (truncated to 10KB)
    error           TEXT,                           -- Error message if connection failed
    latency_ms      INT NOT NULL,                   -- Delivery duration in milliseconds
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_attempts_message_id ON message_attempts (message_id);
CREATE INDEX idx_attempts_endpoint_status ON message_attempts (endpoint_id, status, created_at DESC);
```

### 2.7 endpoint_health

Tracks circuit breaker state per endpoint. One row per endpoint (upserted).

```sql
CREATE TABLE endpoint_health (
    endpoint_id         UUID PRIMARY KEY REFERENCES endpoints(id) ON DELETE CASCADE,
    circuit_state       VARCHAR(20) NOT NULL DEFAULT 'closed',  -- closed, open, half_open
    consecutive_failures INT NOT NULL DEFAULT 0,
    last_failure_at     TIMESTAMPTZ,
    last_success_at     TIMESTAMPTZ,
    cooldown_until      TIMESTAMPTZ,                -- When to transition from open → half_open
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

### 2.8 dashboard_users

Simple authentication for the dashboard UI.

```sql
CREATE TABLE dashboard_users (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email           VARCHAR(255) NOT NULL UNIQUE,
    password_hash   VARCHAR(255) NOT NULL,          -- BCrypt hash
    role            VARCHAR(20) NOT NULL DEFAULT 'admin',  -- admin, viewer
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_login_at   TIMESTAMPTZ
);
```

---

## 3. Data Lifecycle & Retention

### 3.1 Message Retention Policy

Messages and attempts accumulate fast. Retention policies:

| Data | Default Retention | Configurable |
|------|-------------------|-------------|
| Delivered messages | 30 days | Yes |
| Failed messages (dead letter) | 90 days | Yes |
| Message attempts | Same as parent message | Yes |
| Archived event types | Indefinite | No |

### 3.2 Cleanup Job

A periodic background job (daily at 3 AM by default) deletes expired records:

```sql
-- Cleanup delivered messages older than retention period
DELETE FROM messages
WHERE status = 'delivered'
  AND delivered_at < NOW() - INTERVAL '30 days';

-- Cleanup dead letter messages older than retention period
DELETE FROM messages
WHERE status = 'dead_letter'
  AND created_at < NOW() - INTERVAL '90 days';
```

### 3.3 Estimated Storage

| Scale | Messages/day | DB size/month | Notes |
|-------|-------------|---------------|-------|
| Small (startup) | 1,000 | ~50 MB | 30-day retention |
| Medium (SaaS) | 50,000 | ~2.5 GB | 30-day retention |
| Large | 500,000 | ~25 GB | Consider partitioning |

For large deployments, consider PostgreSQL table partitioning on `messages.created_at` (range partitioning by month).

---

## 4. Migration Strategy

EF Core migrations, applied automatically on application startup:

```csharp
// Program.cs
using var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
await db.Database.MigrateAsync();
```

**First migration** creates all tables, indexes, and seeds default dashboard admin user from environment variables.

---

## 5. Key Queries

### 5.1 Queue Poll (Delivery Worker)
```sql
-- Dequeue up to 10 pending messages
WITH next_batch AS (
    SELECT id
    FROM messages
    WHERE status = 'pending'
      AND scheduled_at <= NOW()
    ORDER BY scheduled_at ASC
    LIMIT 10
    FOR UPDATE SKIP LOCKED
)
UPDATE messages m
SET status = 'sending',
    locked_at = NOW(),
    locked_by = @workerId
FROM next_batch
WHERE m.id = next_batch.id
RETURNING m.*;
```

### 5.2 Dashboard — Delivery Stats (Last 24h)
```sql
SELECT
    COUNT(*) FILTER (WHERE status = 'delivered') AS delivered,
    COUNT(*) FILTER (WHERE status = 'failed')    AS failed,
    COUNT(*) FILTER (WHERE status = 'pending')   AS pending,
    COUNT(*) FILTER (WHERE status = 'dead_letter') AS dead_letter,
    AVG(latency_ms) FILTER (WHERE status = 'delivered') AS avg_latency_ms
FROM messages
WHERE app_id = @appId
  AND created_at >= NOW() - INTERVAL '24 hours';
```

### 5.3 Endpoint Health Check (Circuit Breaker)
```sql
SELECT
    endpoint_id,
    COUNT(*) FILTER (WHERE status = 'failed')  AS recent_failures,
    COUNT(*) FILTER (WHERE status = 'success')  AS recent_successes,
    MAX(created_at) FILTER (WHERE status = 'failed') AS last_failure
FROM message_attempts
WHERE endpoint_id = @endpointId
  AND created_at >= NOW() - INTERVAL '10 minutes'
GROUP BY endpoint_id;
```

### 5.4 Stale Lock Recovery
```sql
-- Find messages stuck in 'sending' for > 5 minutes (worker crashed)
UPDATE messages
SET status = 'pending',
    locked_at = NULL,
    locked_by = NULL
WHERE status = 'sending'
  AND locked_at < NOW() - INTERVAL '5 minutes';
```
