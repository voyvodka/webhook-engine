# REST API Reference
# WebhookEngine

**Base URL:** `http://localhost:5100/api/v1`
**Authentication:** `Authorization: Bearer {api_key}`
**Content-Type:** `application/json`

> **Interactive reference:** when running in `Development` or `Staging`, the API host serves an interactive [Scalar](https://scalar.com/) UI at `/scalar` and the raw OpenAPI 3 document at `/openapi/v1.json`. Both routes are unmapped in `Production` deployments.

---

## 1. Authentication

All API endpoints (except dashboard auth) require an Application API key:

```http
Authorization: Bearer whe_app1a2b3_xK9mNpQrStUvWxYz1234567890abcdef
```

Errors:
- `401 Unauthorized` — Missing or invalid API key
- `403 Forbidden` — API key valid but insufficient permissions

---

## 2. Common Response Format

### Success Response
```json
{
  "data": { ... },
  "meta": {
    "requestId": "req_abc123"
  }
}
```

### List Response (Paginated)
```json
{
  "data": [ ... ],
  "meta": {
    "requestId": "req_abc123",
    "pagination": {
      "page": 1,
      "pageSize": 20,
      "totalCount": 156,
      "totalPages": 8,
      "hasNext": true,
      "hasPrev": false
    }
  }
}
```

### Error Response
```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "The endpoint URL is not valid.",
    "details": [
      { "field": "url", "message": "Must be a valid HTTPS URL." }
    ]
  },
  "meta": {
    "requestId": "req_abc123"
  }
}
```

### Error Codes
| HTTP Status | Code | Description |
|-------------|------|-------------|
| 400 | `VALIDATION_ERROR` | Request body validation failed |
| 401 | `UNAUTHORIZED` | Missing or invalid API key |
| 404 | `NOT_FOUND` | Resource does not exist |
| 409 | `CONFLICT` | Duplicate (e.g., idempotency key) |
| 422 | `UNPROCESSABLE` | Request understood but cannot be processed |
| 429 | `RATE_LIMITED` | Too many requests |
| 500 | `INTERNAL_ERROR` | Unexpected server error |

---

## 3. Endpoints

### 3.1 Health Check

#### Health
```
GET /health
GET /api/v1/health
```

No authentication required.

Response: `200 OK`
```json
{
  "status": "healthy"
}
```

---

### 3.2 Applications (Dashboard Auth)

Applications are managed via the dashboard (cookie auth, not API key).

#### Create Application
```
POST /api/v1/applications
```

Request:
```json
{
  "name": "My SaaS App",
  "rateLimitPerSecond": 200,
  "retentionDeliveredDays": 30,
  "retentionDeadLetterDays": 90
}
```

`rateLimitPerSecond` (v0.1.6) overrides the global `WebhookEngine:RateLimit` 1-second sliding-window cap; omit / null ⇒ use global default. `retentionDeliveredDays` and `retentionDeadLetterDays` (v0.1.6) override `WebhookEngine:Retention` for this app only; the cleanup worker partitions its sweep accordingly.

Response: `201 Created`
```json
{
  "data": {
    "id": "...",
    "name": "My SaaS App",
    "apiKey": "whe_abc123_xK9m...",
    "signingSecret": "base64-encoded-secret",
    "isActive": true,
    "rateLimitPerSecond": 200,
    "retentionDeliveredDays": 30,
    "retentionDeadLetterDays": 90,
    "createdAt": "2026-02-26T14:30:00Z"
  }
}
```

Note: `apiKey` is only returned on creation. Store it securely — it cannot be retrieved again.

#### List Applications
```
GET /api/v1/applications
    ?page=1
    &pageSize=20
```

#### Get Application
```
GET /api/v1/applications/{applicationId}
```

#### Update Application
```
PUT /api/v1/applications/{applicationId}
```

Accepts the same `name`, `rateLimitPerSecond`, `retentionDeliveredDays`, and `retentionDeadLetterDays` fields. Sending `null` for an override field clears it (back to global default).

#### Delete Application
```
DELETE /api/v1/applications/{applicationId}
```

Cascades to bound endpoints, event types, and messages. The `audit_logs` trail is preserved (no FK), and the audit row carries `beforeSnapshot.messageCount` for forensics.

#### Rotate API Key
```
POST /api/v1/applications/{applicationId}/rotate-key
```

Generates a new API key. The old key is immediately invalidated.

#### Rotate Signing Secret
```
POST /api/v1/applications/{applicationId}/rotate-secret
```

Generates a new HMAC signing secret.

---

### 3.3 Event Types

#### Create Event Type
```
POST /api/v1/event-types
```

Request:
```json
{
  "name": "order.created",
  "description": "Fired when a new order is placed",
  "idempotencyWindowMinutes": 60
}
```

`idempotencyWindowMinutes` (v0.1.6) overrides the per-app `IdempotencyOptions.WindowMinutes` for this event type only — useful when a high-volume event needs a tighter window or a low-volume one a looser one. Omit / null ⇒ use the per-app default.

Response: `201 Created`
```json
{
  "data": {
    "id": "evt_abc123",
    "name": "order.created",
    "description": "Fired when a new order is placed",
    "idempotencyWindowMinutes": 60,
    "createdAt": "2026-02-26T14:30:00Z"
  }
}
```

#### List Event Types
```
GET /api/v1/event-types
    ?page=1
    &pageSize=20
    &includeArchived=false
```

Response: `200 OK` (paginated list)

#### Get Event Type
```
GET /api/v1/event-types/{eventTypeId}
```

#### Update Event Type
```
PUT /api/v1/event-types/{eventTypeId}
```

#### Archive Event Type
```
DELETE /api/v1/event-types/{eventTypeId}
```
Note: Soft delete (archive). Existing messages referencing this type are unaffected.

---

### 3.4 Endpoints (Webhook Receivers)

#### Create Endpoint
```
POST /api/v1/endpoints
```

Request:
```json
{
  "url": "https://api.customer.com/webhooks",
  "description": "Customer A production webhook",
  "filterEventTypes": ["evt_abc123", "evt_def456"],
  "customHeaders": {
    "X-Custom-Auth": "secret123"
  },
  "metadata": {
    "customerId": "cust_001",
    "environment": "production"
  },
  "allowedIps": ["203.0.113.0/24", "2001:db8::/32"],
  "transformExpression": "{ id: orderId, total: amount }",
  "transformEnabled": false
}
```

The `url` host is DNS-resolved at create / update time and rejected if any resolved address sits inside RFC1918, loopback, link-local, CGNAT, cloud-metadata, or IPv6 unique-local / link-local / IPv4-mapped private ranges. The same rules fire again at connect time inside `SocketsHttpHandler.ConnectCallback` — defeats DNS rebinding.

`allowedIps` is an optional CIDR positive-list (IPv4 and IPv6). When set, deliveries only fire if every resolved address sits inside at least one allowed CIDR. Empty / absent ⇒ "no allowlist" (default).

`transformExpression` / `transformEnabled` configure per-endpoint JMESPath payload transformation (ADR-003); see `POST /api/v1/dashboard/transform/validate` for live validation.

Response: `201 Created`
```json
{
  "data": {
    "id": "ep_xyz789",
    "url": "https://api.customer.com/webhooks",
    "description": "Customer A production webhook",
    "status": "active",
    "filterEventTypes": ["evt_abc123", "evt_def456"],
    "customHeaders": { "X-Custom-Auth": "***" },
    "metadata": { "customerId": "cust_001", "environment": "production" },
    "health": {
      "state": "healthy",
      "consecutiveFailures": 0,
      "lastSuccessAt": null,
      "lastFailureAt": null
    },
    "createdAt": "2026-02-26T14:30:00Z"
  }
}
```

Notes:
- If `filterEventTypes` is empty or omitted, endpoint receives ALL event types.
- `customHeaders` values are masked in GET responses (`***`).

#### List Endpoints
```
GET /api/v1/endpoints
    ?page=1
    &pageSize=20
    &status=active         (active | disabled)
```

#### Get Endpoint
```
GET /api/v1/endpoints/{endpointId}
```

Includes health status.

#### Update Endpoint
```
PUT /api/v1/endpoints/{endpointId}
```

#### Disable Endpoint
```
POST /api/v1/endpoints/{endpointId}/disable
```

#### Enable Endpoint
```
POST /api/v1/endpoints/{endpointId}/enable
```

#### Delete Endpoint
```
DELETE /api/v1/endpoints/{endpointId}
```

#### Get Endpoint Stats
```
GET /api/v1/endpoints/{endpointId}/stats
    ?period=24h            (1h | 24h | 7d | 30d)
```

Response: `200 OK`
```json
{
  "data": {
    "endpointId": "ep_xyz789",
    "period": "24h",
    "totalAttempts": 1250,
    "successful": 1230,
    "failed": 20,
    "successRate": 98.4,
    "avgLatencyMs": 145,
    "p95LatencyMs": 320
  }
}
```

---

### 3.5 Messages (Sending Webhooks)

#### Send Message
```
POST /api/v1/messages
```

This is the primary API. Your application calls this when an event occurs.

Request:
```json
{
  "eventType": "order.created",
  "payload": {
    "orderId": "ord_abc123",
    "amount": 99.99,
    "currency": "TRY",
    "customer": {
      "id": "cust_001",
      "email": "user@example.com"
    }
  },
  "eventId": "evt_unique_001",
  "idempotencyKey": "idem_order_abc123_created"
}
```

Response: `202 Accepted`
```json
{
  "data": {
    "messageIds": [
      "msg_aaa111",
      "msg_bbb222"
    ],
    "endpointCount": 2,
    "eventType": "order.created"
  }
}
```

Notes:
- Returns `202 Accepted` (not `201 Created`) because delivery is async.
- `messageIds` — one per subscribed endpoint (fan-out).
- `eventId` — optional client-side event identifier for correlation.
- `idempotencyKey` — optional. If provided, duplicate sends with same key return the original response (within 24h window).

#### Batch Send Messages
```
POST /api/v1/messages/batch
```

Request:
```json
{
  "messages": [
    {
      "eventType": "order.created",
      "payload": { "orderId": "ord_001" },
      "eventId": "evt_001"
    },
    {
      "eventTypeId": "0f3e8c63-4fef-4f4f-8f2f-2df0b0d61c11",
      "payload": { "orderId": "ord_002" }
    }
  ]
}
```

Response: `202 Accepted`
```json
{
  "data": {
    "totalEvents": 2,
    "acceptedEvents": 2,
    "rejectedEvents": 0,
    "totalEnqueuedMessages": 4,
    "results": [
      {
        "index": 0,
        "success": true,
        "eventType": "order.created",
        "endpointCount": 2,
        "messageIds": ["..."]
      }
    ]
  }
}
```

#### Replay Messages
```
POST /api/v1/messages/replay
```

Request:
```json
{
  "eventType": "order.created",
  "from": "2026-02-25T00:00:00Z",
  "to": "2026-02-26T23:59:59Z",
  "statuses": ["delivered", "failed"],
  "maxMessages": 100
}
```

Response: `202 Accepted`
```json
{
  "data": {
    "sourceCount": 18,
    "replayedCount": 18,
    "messageIds": ["..."],
    "eventType": "order.created",
    "endpointId": null,
    "from": "2026-02-25T00:00:00Z",
    "to": "2026-02-26T23:59:59Z",
    "maxMessages": 100,
    "statuses": ["delivered", "failed"]
  }
}
```

#### Get Message
```
GET /api/v1/messages/{messageId}
```

Response: `200 OK`
```json
{
  "data": {
    "id": "msg_aaa111",
    "eventType": "order.created",
    "endpoint": {
      "id": "ep_xyz789",
      "url": "https://api.customer.com/webhooks"
    },
    "payload": { ... },
    "status": "delivered",
    "attemptCount": 1,
    "maxRetries": 7,
    "scheduledAt": "2026-02-26T14:30:00Z",
    "deliveredAt": "2026-02-26T14:30:00.342Z",
    "createdAt": "2026-02-26T14:30:00Z"
  }
}
```

#### List Messages
```
GET /api/v1/messages
    ?page=1
    &pageSize=20
    &status=delivered      (pending | sending | delivered | failed | deadletter)
    &eventTypeId={uuid}
    &endpointId={uuid}
    &before=2026-02-26T23:59:59Z
    &after=2026-02-25T00:00:00Z
```

#### Retry Message
```
POST /api/v1/messages/{messageId}/retry
```

Resets message status to `pending` and schedules immediate delivery. Works for `failed` and `deadletter` messages.

Response: `200 OK`
```json
{
  "data": {
    "messageId": "msg_aaa111",
    "status": "pending",
    "scheduledAt": "2026-02-26T15:00:00Z"
  }
}
```

---

### 3.6 Message Attempts

#### List Attempts for a Message
```
GET /api/v1/messages/{messageId}/attempts
    ?page=1
    &pageSize=20
```

Response: `200 OK`
```json
{
  "data": [
    {
      "id": "att_001",
      "attemptNumber": 1,
      "status": "failed",
      "statusCode": 500,
      "responseBody": "{\"error\":\"internal server error\"}",
      "latencyMs": 2340,
      "createdAt": "2026-02-26T14:30:00Z"
    },
    {
      "id": "att_002",
      "attemptNumber": 2,
      "status": "success",
      "statusCode": 200,
      "responseBody": "{\"received\":true}",
      "latencyMs": 145,
      "createdAt": "2026-02-26T14:30:05Z"
    }
  ]
}
```

---

### 3.7 Dashboard API (Internal)

These endpoints power the React dashboard. Authenticated via dashboard session cookie (not API key).

#### Dashboard Overview
```
GET /api/v1/dashboard/overview
```

Response:
```json
{
  "data": {
    "last24h": {
      "totalMessages": 12500,
      "delivered": 12350,
      "failed": 120,
      "pending": 30,
      "deadLetter": 0,
      "successRate": 98.8,
      "avgLatencyMs": 156
    },
    "endpoints": {
      "total": 25,
      "healthy": 22,
      "degraded": 2,
      "failed": 1,
      "disabled": 0
    },
    "queueDepth": 30
  }
}
```

#### Dashboard — Delivery Timeline (Chart Data)
```
GET /api/v1/dashboard/timeline
    ?period=24h            (1h | 24h | 7d | 30d)
    &interval=1h           (5m | 1h | 1d)
```

Response:
```json
{
  "data": {
    "buckets": [
      { "timestamp": "2026-02-26T00:00:00Z", "delivered": 520, "failed": 5 },
      { "timestamp": "2026-02-26T01:00:00Z", "delivered": 480, "failed": 3 },
      ...
    ]
  }
}
```

#### Dashboard — Endpoints (Cross-App)
```
GET /api/v1/dashboard/endpoints
    ?appId={uuid}
    &status=active
    &page=1
    &pageSize=20
```

#### Dashboard — Create Endpoint
```
POST /api/v1/dashboard/endpoints
```

Request body same as `POST /api/v1/endpoints` but with additional `appId` field.

#### Dashboard — Update Endpoint
```
PUT /api/v1/dashboard/endpoints/{endpointId}
```

#### Dashboard — Disable Endpoint
```
POST /api/v1/dashboard/endpoints/{endpointId}/disable
```

#### Dashboard — Enable Endpoint
```
POST /api/v1/dashboard/endpoints/{endpointId}/enable
```

#### Dashboard — Delete Endpoint
```
DELETE /api/v1/dashboard/endpoints/{endpointId}
```

#### Dashboard — Send Test Webhook (v0.1.6)
```
POST /api/v1/dashboard/endpoints/{endpointId}/test
```

Sends a fully-signed test webhook to the endpoint URL **without enqueueing a `Message` row**. Returns the receiver's response and the exact request the engine sent.

Request:
```json
{
  "eventType": "order.created",
  "payload": { "orderId": 42, "amount": 99.99 },
  "customHeaders": { "X-Trace": "manual-test" }
}
```

`payload`, `eventType`, and `customHeaders` are all optional — defaults produce a minimal `{ "test": true }` payload signed with the endpoint's secret.

Response: `200 OK`
```json
{
  "data": {
    "request": {
      "url": "https://api.customer.com/webhooks",
      "method": "POST",
      "headers": { "webhook-id": "msg_test_...", "webhook-timestamp": "...", "webhook-signature": "v1,..." },
      "body": "{\"orderId\":42,\"amount\":99.99}"
    },
    "response": {
      "statusCode": 200,
      "body": "{\"ok\":true}",
      "latencyMs": 142
    }
  }
}
```

If the receiver is unreachable, `statusCode` is `0` and `body` carries the connection error message. The endpoint's circuit breaker counters are **not** affected by test deliveries.

#### Dashboard — Validate Transform Expression (v0.1.4)
```
POST /api/v1/dashboard/transform/validate
```

Endpoint-agnostic helper that evaluates a JMESPath expression against a sample payload and returns either the transformed result or the parser error. Reuses the same `IPayloadTransformer` (and timeout / size guards) that runs in the delivery pipeline.

Request:
```json
{
  "expression": "{ id: orderId, total: amount }",
  "samplePayload": { "orderId": 42, "amount": 99.99, "currency": "USD" }
}
```

Response: `200 OK`
```json
{
  "data": {
    "success": true,
    "result": { "id": 42, "total": 99.99 },
    "error": null
  }
}
```

`200` with `success: false` indicates the expression parsed but produced an error during evaluation; `422` indicates the expression itself is invalid.

#### Dashboard — Audit Log (v0.1.6)
```
GET /api/v1/dashboard/audit
    ?applicationId={uuid}
    &entityType=application      (application | endpoint | event_type | message)
    &entityId={uuid}
    &action=updated              (created | updated | deleted | rotated_key | replayed | retried | tested)
    &from=2026-05-01T00:00:00Z
    &to=2026-05-08T23:59:59Z
    &page=1
    &pageSize=50
```

Response:
```json
{
  "data": [
    {
      "id": "aud_abc123",
      "actorEmail": "admin@example.com",
      "applicationId": "app_xyz789",
      "entityType": "endpoint",
      "entityId": "ep_xyz789",
      "action": "updated",
      "beforeSnapshot": { "url": "https://old.example.com" },
      "afterSnapshot": { "url": "https://new.example.com" },
      "requestId": "req_a1b2c3",
      "createdAt": "2026-05-08T10:30:00Z"
    }
  ],
  "meta": { "pagination": { "page": 1, "pageSize": 50, "totalCount": 312 } }
}
```

The table is **append-only** — there is no `DELETE` route. Rows survive cascades (deleting an application does **not** remove its audit history), so post-incident reconstruction works even after the parent entity is gone. On application delete, `beforeSnapshot.messageCount` carries the row count of bound messages so the cascade is auditable.

#### Dashboard — Event Types
```
GET /api/v1/dashboard/event-types
    ?appId={uuid}
    &includeArchived=false
```

#### Dashboard — Create Event Type
```
POST /api/v1/dashboard/event-types
```

#### Dashboard — Update Event Type
```
PUT /api/v1/dashboard/event-types/{eventTypeId}
```

#### Dashboard — Archive Event Type
```
DELETE /api/v1/dashboard/event-types/{eventTypeId}
```

#### Dashboard — Messages (Cross-App)
```
GET /api/v1/dashboard/messages
    ?appId={uuid}
    &status=delivered
    &endpointId={uuid}
    &eventType=order.created
    &after=2026-02-25T00:00:00Z
    &before=2026-02-26T23:59:59Z
    &page=1
    &pageSize=20
```

#### Dashboard — Get Message Detail
```
GET /api/v1/dashboard/messages/{messageId}
```

Returns message with attempts included.

#### Dashboard — Send Test Message
```
POST /api/v1/dashboard/messages/send
```

#### Dashboard — Retry Message
```
POST /api/v1/dashboard/messages/{messageId}/retry
```

#### Dashboard Auth — Login
```
POST /api/v1/auth/login
```

Request:
```json
{
  "email": "admin@example.com",
  "password": "changeme"
}
```

Response: `200 OK` + HttpOnly session cookie

#### Dashboard Auth — Logout
```
POST /api/v1/auth/logout
```

#### Dashboard Auth — Me
```
GET /api/v1/auth/me
```

---

## 4. SignalR — Live Dashboard Events

The dashboard streams real-time events over SignalR at `/hubs/deliveries` (cookie-authenticated, same session as the dashboard). The hub is server-to-client only — the client does not invoke any methods.

| Event | Payload | When |
|---|---|---|
| `DeliverySuccess` | `{ messageId, endpointId, statusCode, latencyMs }` | A delivery attempt returned 2xx |
| `DeliveryFailure` | `{ messageId, endpointId, statusCode, error, attemptCount }` | A delivery attempt failed (will retry or dead-letter) |
| `DeadLetter` | `{ messageId, endpointId, finalAttempt, reason }` | Message exhausted its retry budget |
| `EndpointHealthChanged` (v0.1.6) | `{ endpointId, status, circuitState, consecutiveFailures, cooldownUntilUtc }` | `EndpointHealthTracker` mutated either the visible endpoint status or the circuit-breaker state |

The dashboard treats `EndpointHealthChanged` as a cache-invalidation signal — TanStack Query refetches the endpoints list rather than patching local state, so health badges stay correct across multiple tabs / sessions.

---

### 3.8 Portal API (Customer-Facing JWT) — v0.2.0

The portal API is a narrowed mirror of the public endpoint surface, scoped to a single application via a short-lived JWT minted by the host SaaS. It powers the embeddable `<EndpointManager />` React component (`@webhookengine/endpoint-manager` on npm). The engine **only verifies** these tokens — it never mints them. Per-application signing key, allowed CORS origins, and capability set are managed by the operator from the dashboard.

For host-side integration (token mint, CSS theming, sample app), see `docs/PORTAL.md`. This section is the wire reference.

#### Authentication

Every request to `/api/v1/portal/*` (except `OPTIONS` preflight) requires a Bearer JWT in `Authorization: Bearer <token>`.

- **Algorithm:** HS256 only. `alg=none` and HS384/HS512 are rejected with `PORTAL_AUTH_INVALID_SIGNATURE`.
- **Signing key:** per-application `PortalSigningKey` (32 bytes minimum). Generated at portal-enable time; never returned by the engine after creation. Rotated via the dashboard rotate action.
- **Lifetime cap:** `exp - nbf <= 15 minutes` (configurable via `WebhookEngine:PortalAuth:MaxLifetimeMinutes`). Tokens with longer requested lifetimes are rejected as `PORTAL_AUTH_LIFETIME_TOO_LONG` even when currently valid.
- **Clock skew:** ±30 s (`PortalAuth:ClockSkewSeconds`).
- **Token size cap:** 8 KiB (`PortalAuth:MaxTokenSizeBytes`). Larger payloads are rejected before parsing.
- **Required claims:** `appId` (UUID — selects the signing key), `nbf`, `exp`. `sub` is recommended, `iat` is optional. Repeated `capabilities` claims grant scope (see below).

#### Capabilities

Tokens are scoped by repeated `capabilities` claims (colon-delimited wire format). Missing capability → `403 PORTAL_INSUFFICIENT_CAPABILITY`. **Absence of any `capabilities` claim grants nothing**, not full access.

| Capability | Grants |
|---|---|
| `endpoints:read` | `GET /endpoints`, `GET /endpoints/{id}`, `GET /event-types` |
| `endpoints:write` | `POST /endpoints`, `PUT /endpoints/{id}`, `DELETE /endpoints/{id}`, `/enable`, `/disable` |
| `endpoints:test` | `POST /endpoints/{id}/test` (highest-risk — fires real outbound HTTP) |
| `attempts:read` | `GET /endpoints/{id}/attempts` |

#### CORS

Per-application allowed origins are stored on `Application.AllowedPortalOriginsJson` and managed via `PUT /api/v1/dashboard/applications/{appId}/portal/origins`.

- Wildcards are **not** supported — host SaaS must enumerate exact origins.
- HTTPS-only outside Development. Up to 50 origins per app, 256 chars each.
- Origin matching is RFC 6454 case-insensitive on scheme + host.
- `OPTIONS` preflight returns `204` with `Access-Control-Allow-Origin: <echoed origin>`, `Allow-Methods`, `Allow-Headers: Authorization, Content-Type`, `Max-Age: 600`. A disallowed origin returns `403` with no CORS headers (so the browser correctly surfaces a CORS error).

#### Rate limiting

Portal routes share the public API's `send-by-appid` token-bucket partition. The portal token's `appId` flows into the limiter via `HttpContext.Items["AppId"]`. A 429 carries the standard `Retry-After` header.

#### Cross-tenant isolation

Every resource lookup is scoped via the 2-arg `GetByIdAsync(appId, endpointId)` repository method. A token for tenant A asking for tenant B's endpoint id receives **`404 PORTAL_NOT_FOUND`** (never 403 — that would leak the existence of resources owned by other apps).

#### Routes

##### List Endpoints
```
GET /api/v1/portal/endpoints
    ?status=active|degraded|failed|disabled
    &page=1
    &pageSize=20
```

Response shape strips `secretOverride` (returns `hasSecretOverride: bool` instead) and full custom-header values (returns `customHeaderNames: string[]`).

##### Get Endpoint
```
GET /api/v1/portal/endpoints/{endpointId}
```

Strips `transformExpression`, `transformEnabled`, `transformValidatedAt`, `allowedIpsJson` — these are admin-only fields.

##### Create Endpoint
```
POST /api/v1/portal/endpoints
```
```json
{
  "url": "https://api.acme.example/webhooks/orders",
  "description": "Order lifecycle events",
  "filterEventTypes": ["uuid-of-event-type"],
  "customHeaders": { "X-Source": "webhookengine" },
  "metadata": { "team": "growth" },
  "secretOverride": "whsec_AbCdEf01234567890aBcDeF0123456789"
}
```

`url` must pass the SSRF-hardened URL policy (HTTPS, public DNS, no private/loopback IPs at validate-time and at connect-time). `secretOverride` requires the `whsec_` prefix and ≥32 chars — typing a weak password is rejected with `422 PORTAL_VALIDATION_FAILED`. `transformExpression` / `allowedIpsJson` are not exposed; if smuggled into the body, model binding drops them silently.

##### Update Endpoint
```
PUT /api/v1/portal/endpoints/{endpointId}
```

Partial replace — every field is optional, only non-null fields are applied. `filterEventTypes`, when provided, replaces the full list (clear by sending `[]`). At least one field must be present.

##### Delete Endpoint
```
DELETE /api/v1/portal/endpoints/{endpointId}
```

Returns `204 No Content` on success.

##### Enable / Disable Endpoint
```
POST /api/v1/portal/endpoints/{endpointId}/enable
POST /api/v1/portal/endpoints/{endpointId}/disable
```

Returns `200 OK` with the updated endpoint detail.

##### Send Test Webhook
```
POST /api/v1/portal/endpoints/{endpointId}/test
```
```json
{
  "eventType": "order.created",
  "payload": { "orderId": "ord_abc123" }
}
```

Fires a real outbound HTTP POST through the engine's `webhook-delivery` HttpClient (HMAC-signed, SSRF-checked). Returns the request preview, response status, latency, and body. **Does not** affect endpoint health or retention; the dispatch never enters the persistent queue.

##### List Attempts for an Endpoint
```
GET /api/v1/portal/endpoints/{endpointId}/attempts
    ?page=1
    &pageSize=20
```

Most-recent-first delivery attempts for the endpoint. `attempts:read` capability required.

##### List Event Types
```
GET /api/v1/portal/event-types
    ?page=1
    &pageSize=100
```

Read-only dropdown source for the embedded UI. Archived event types are excluded; their lifecycle is admin-only.

#### Dashboard portal-admin routes

These cookie-authenticated dashboard routes manage the portal grant per application:

```
GET  /api/v1/dashboard/applications/{appId}/portal
POST /api/v1/dashboard/applications/{appId}/portal/enable
POST /api/v1/dashboard/applications/{appId}/portal/rotate
POST /api/v1/dashboard/applications/{appId}/portal/disable
PUT  /api/v1/dashboard/applications/{appId}/portal/origins
```

`enable` and `rotate` return the new `portalSigningKey` **once** — capture it on the host SaaS (it's never returned again). `disable` clears the signing key (in-flight tokens are rejected within `PortalAuth:LookupCacheTtlSeconds` on remote nodes; instantly on the local node via the lookup-cache invalidation hook). Audit log records every mutating action with the signing key redacted to `portalEnabled: bool`.

#### Error codes (portal-specific)

| Code | HTTP | Meaning |
|---|---|---|
| `PORTAL_AUTH_REQUIRED` | 401 | Missing or malformed `Authorization: Bearer` header. |
| `PORTAL_AUTH_INVALID_TOKEN` | 401 | JWT is malformed, oversized (>8 KiB), or fails post-parse validation. |
| `PORTAL_AUTH_INVALID_SIGNATURE` | 401 | Wrong key, wrong algorithm, or `alg=none`. |
| `PORTAL_AUTH_TOKEN_EXPIRED` | 401 | `exp` is in the past beyond clock skew. |
| `PORTAL_AUTH_LIFETIME_TOO_LONG` | 401 | `exp - nbf` exceeds `MaxLifetimeMinutes`. |
| `PORTAL_NOT_ENABLED` | 401 | App exists but `PortalSigningKey` is null. |
| `PORTAL_INSUFFICIENT_CAPABILITY` | 403 | Token lacks the capability required by the route. |
| `PORTAL_NOT_FOUND` | 404 | Endpoint/event-type not found in this tenant's scope. |
| `PORTAL_VALIDATION_FAILED` | 422 | Request body failed FluentValidation. |

#### cURL — end-to-end probe

Mint a token on the host SaaS (Node.js example below) then call the portal:

```js
// Server-side (host SaaS), Node.js + jose
import { SignJWT } from 'jose';
const secret = new TextEncoder().encode(process.env.PORTAL_SIGNING_KEY);
const token = await new SignJWT({
  appId: '00000000-0000-0000-0000-000000000001',
  capabilities: ['endpoints:read', 'endpoints:write', 'endpoints:test', 'attempts:read'],
})
  .setProtectedHeader({ alg: 'HS256' })
  .setNotBefore('0s')
  .setExpirationTime('10m')
  .sign(secret);
```

```bash
# List endpoints
curl https://hooks.example.com/api/v1/portal/endpoints \
  -H "Authorization: Bearer $TOKEN" \
  -H "Origin: https://app.acme.example"

# Send a test webhook
curl -X POST https://hooks.example.com/api/v1/portal/endpoints/{id}/test \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"eventType":"order.created","payload":{"orderId":"ord_abc"}}'
```

---

## 5. Webhook Headers Sent to Endpoints

Every webhook delivery includes these standard headers:

| Header | Example | Description |
|--------|---------|-------------|
| `Content-Type` | `application/json` | Always JSON |
| `User-Agent` | `WebhookEngine/1.0` | Identifies sender |
| `webhook-id` | `msg_aaa111` | Unique message identifier |
| `webhook-timestamp` | `1740600000` | Unix timestamp (seconds) |
| `webhook-signature` | `v1,K7gNU3sdo+OL...` | HMAC-SHA256 signature |

Plus any custom headers configured on the endpoint.

---

## 6. Rate Limits

| Scope | Limit | Header |
|-------|-------|--------|
| Per API Key | 1000 requests/minute | `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset` |
| Message sending | 100 messages/second per app | Same headers |

When rate limited: `429 Too Many Requests` with `Retry-After` header.

---

## 7. SDK Usage Examples

### C# SDK (NuGet)
```csharp
var client = new WebhookEngineClient("whe_app1a2b3_xK9m...", "http://localhost:5100");

// Send a webhook
await client.Messages.SendAsync(new SendMessageRequest
{
    EventType = "order.created",
    Payload = new { OrderId = "ord_abc123", Amount = 99.99m },
    IdempotencyKey = "idem_order_abc123_created"
});

// List endpoints
var endpoints = await client.Endpoints.ListAsync(page: 1, pageSize: 20);

// Retry a failed message
await client.Messages.RetryAsync("msg_aaa111");
```

### TypeScript SDK (npm)
TypeScript SDK is planned for Phase 2 and is not published yet.
Use the REST API examples below (or the .NET SDK above) until npm package release.

### cURL
```bash
# Send a webhook
curl -X POST http://localhost:5100/api/v1/messages \
  -H "Authorization: Bearer whe_app1a2b3_xK9m..." \
  -H "Content-Type: application/json" \
  -d '{
    "eventType": "order.created",
    "payload": {"orderId": "ord_abc123", "amount": 99.99}
  }'
```
