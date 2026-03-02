# REST API Reference
# WebhookEngine

**Base URL:** `http://localhost:5100/api/v1`
**Authentication:** `Authorization: Bearer {api_key}`
**Content-Type:** `application/json`

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
  "name": "My SaaS App"
}
```

Response: `201 Created`
```json
{
  "data": {
    "id": "...",
    "name": "My SaaS App",
    "apiKey": "whe_abc123_xK9m...",
    "signingSecret": "base64-encoded-secret",
    "isActive": true,
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

#### Delete Application
```
DELETE /api/v1/applications/{applicationId}
```

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
  "description": "Fired when a new order is placed"
}
```

Response: `201 Created`
```json
{
  "data": {
    "id": "evt_abc123",
    "name": "order.created",
    "description": "Fired when a new order is placed",
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
  }
}
```

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
    &status=delivered      (pending | sending | delivered | failed | dead_letter)
    &eventTypeId={uuid}
    &endpointId={uuid}
    &before=2026-02-26T23:59:59Z
    &after=2026-02-25T00:00:00Z
```

#### Retry Message
```
POST /api/v1/messages/{messageId}/retry
```

Resets message status to `pending` and schedules immediate delivery. Works for `failed` and `dead_letter` messages.

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

## 4. Webhook Headers Sent to Endpoints

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

## 5. Rate Limits

| Scope | Limit | Header |
|-------|-------|--------|
| Per API Key | 1000 requests/minute | `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset` |
| Message sending | 100 messages/second per app | Same headers |

When rate limited: `429 Too Many Requests` with `Retry-After` header.

---

## 6. SDK Usage Examples

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
```typescript
import { WebhookEngine } from '@webhookengine/sdk';

const client = new WebhookEngine('whe_app1a2b3_xK9m...', {
  baseUrl: 'http://localhost:5100'
});

// Send a webhook
await client.messages.send({
  eventType: 'order.created',
  payload: { orderId: 'ord_abc123', amount: 99.99 },
  idempotencyKey: 'idem_order_abc123_created'
});

// List endpoints
const endpoints = await client.endpoints.list({ page: 1, pageSize: 20 });
```

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
