# Getting Started with WebhookEngine

This guide walks you through setting up WebhookEngine, creating an application, registering endpoints, and sending your first webhook -- all in under 10 minutes.

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/) and Docker Compose

That's it. WebhookEngine runs as a single container alongside PostgreSQL.

## 1. Start WebhookEngine

```bash
git clone https://github.com/voyvodka/webhook-engine.git
cd webhook-engine
docker compose -f docker/docker-compose.yml up -d
```

WebhookEngine is now running at **http://localhost:5100**.

## 2. Log in to the Dashboard

Open http://localhost:5100 in your browser.

Default credentials:

| Field    | Value               |
|----------|---------------------|
| Email    | `admin@example.com` |
| Password | `changeme`          |

> Change these in production by setting `WebhookEngine__DashboardAuth__AdminEmail` and `WebhookEngine__DashboardAuth__AdminPassword` environment variables.

## 3. Create an Application

An **application** represents a tenant or service that sends webhooks. Each application gets its own API key.

1. In the dashboard, go to **Applications**
2. Click **Create Application**
3. Enter a name (e.g. "My SaaS App")
4. Copy the generated **API key** (format: `whe_{appId}_{random}`) -- you won't see it again

Or via API:

```bash
# Create an application (requires dashboard cookie auth)
curl -X POST http://localhost:5100/api/v1/applications \
  -H "Content-Type: application/json" \
  -b "your-session-cookie" \
  -d '{"name": "My SaaS App"}'
```

## 4. Create an Event Type

**Event types** categorize webhooks (e.g. `order.created`, `user.updated`, `payment.failed`).

```bash
export API_KEY="whe_abc123_your-api-key"

curl -X POST http://localhost:5100/api/v1/event-types \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "order.created",
    "description": "Fired when a new order is placed"
  }'
```

Response:

```json
{
  "data": {
    "id": "a1b2c3d4-...",
    "name": "order.created",
    "description": "Fired when a new order is placed",
    "isArchived": false,
    "createdAt": "2026-02-27T10:00:00Z"
  },
  "meta": { "requestId": "req_..." }
}
```

## 5. Register an Endpoint

An **endpoint** is a URL that receives webhook deliveries. Endpoints can filter by event type.

```bash
curl -X POST http://localhost:5100/api/v1/endpoints \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "url": "https://your-app.example.com/webhook",
    "description": "Production webhook handler",
    "filterEventTypes": ["a1b2c3d4-..."]
  }'
```

> If `filterEventTypes` is omitted or empty, the endpoint receives **all** event types.

## 6. Send a Webhook

```bash
curl -X POST http://localhost:5100/api/v1/messages \
  -H "Authorization: Bearer $API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "eventType": "order.created",
    "payload": {
      "orderId": "ORD-001",
      "amount": 99.99,
      "currency": "USD"
    },
    "idempotencyKey": "order-001"
  }'
```

Response (HTTP 202 Accepted):

```json
{
  "data": {
    "messageIds": ["msg_..."],
    "endpointCount": 1,
    "eventType": "order.created"
  },
  "meta": { "requestId": "req_..." }
}
```

The message is now queued. WebhookEngine's delivery worker will pick it up and deliver it to all matching endpoints within seconds.

## 7. Check Delivery Status

```bash
curl http://localhost:5100/api/v1/messages/{messageId} \
  -H "Authorization: Bearer $API_KEY"
```

Message status values:

| Status | Meaning |
|--------|---------|
| `pending` | Queued, waiting for delivery |
| `sending` | Currently being delivered |
| `delivered` | Successfully delivered (HTTP 2xx) |
| `failed` | Delivery failed, will retry |
| `deadletter` | All retries exhausted |

View delivery attempts:

```bash
curl http://localhost:5100/api/v1/messages/{messageId}/attempts \
  -H "Authorization: Bearer $API_KEY"
```

## 8. Verify Webhook Signatures

Every webhook delivery includes HMAC-SHA256 signature headers following the [Standard Webhooks](https://www.standardwebhooks.com/) spec:

| Header | Example |
|--------|---------|
| `webhook-id` | `msg_abc123` |
| `webhook-timestamp` | `1709042400` (Unix seconds) |
| `webhook-signature` | `v1,K6x9h3...` (base64) |

The signed content is: `{webhook-id}.{webhook-timestamp}.{body}`

### Python

```python
import hmac, hashlib, base64

def verify(body: str, secret: str, headers: dict) -> bool:
    signed = f"{headers['webhook-id']}.{headers['webhook-timestamp']}.{body}"
    expected = hmac.new(secret.encode(), signed.encode(), hashlib.sha256).digest()
    expected_sig = f"v1,{base64.b64encode(expected).decode()}"
    return hmac.compare_digest(headers['webhook-signature'], expected_sig)
```

### TypeScript

```typescript
import { createHmac } from "crypto";

function verify(body: string, secret: string, headers: Record<string, string>): boolean {
  const signed = `${headers["webhook-id"]}.${headers["webhook-timestamp"]}.${body}`;
  const hash = createHmac("sha256", secret).update(signed).digest("base64");
  return headers["webhook-signature"] === `v1,${hash}`;
}
```

### C#

```csharp
using System.Security.Cryptography;
using System.Text;

bool Verify(string body, string secret, IDictionary<string, string> headers)
{
    var signed = $"{headers["webhook-id"]}.{headers["webhook-timestamp"]}.{body}";
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signed));
    var expected = $"v1,{Convert.ToBase64String(hash)}";
    return headers["webhook-signature"] == expected;
}
```

> For production use, see the complete verification helpers in [`samples/signature-verification/`](../samples/signature-verification/) which include timestamp tolerance checks and constant-time comparison.

## Using the .NET SDK

Install the NuGet package (or reference the project directly):

```csharp
using WebhookEngine.Sdk;

using var client = new WebhookEngineClient("whe_abc_your-api-key", "http://localhost:5100");

// Create event type
var eventType = await client.EventTypes.CreateAsync(new CreateEventTypeRequest
{
    Name = "invoice.paid",
    Description = "Invoice payment received"
});

// Create endpoint
var endpoint = await client.Endpoints.CreateAsync(new CreateEndpointRequest
{
    Url = "https://your-app.example.com/webhook",
    FilterEventTypes = [eventType!.Id]
});

// Send webhook
var result = await client.Messages.SendAsync(new SendMessageRequest
{
    EventType = "invoice.paid",
    Payload = new { invoiceId = "INV-001", amount = 250.00 }
});

Console.WriteLine($"Sent to {result!.EndpointCount} endpoint(s)");
```

See the full sample app in [`samples/WebhookEngine.Sample.Sender/`](../samples/WebhookEngine.Sample.Sender/).

## Retry Policy

Failed deliveries are automatically retried with exponential backoff:

| Attempt | Delay | Cumulative |
|---------|-------|------------|
| 1 | 5 seconds | 5s |
| 2 | 30 seconds | 35s |
| 3 | 2 minutes | ~2.5 min |
| 4 | 15 minutes | ~17.5 min |
| 5 | 1 hour | ~1.3 hr |
| 6 | 6 hours | ~7.3 hr |
| 7 | 24 hours | ~31.3 hr |

After all 7 attempts, the message moves to **dead letter** status. You can manually retry dead-letter messages:

```bash
curl -X POST http://localhost:5100/api/v1/messages/{messageId}/retry \
  -H "Authorization: Bearer $API_KEY"
```

## Circuit Breaker

WebhookEngine tracks endpoint health. If an endpoint fails **5 consecutive** deliveries, its circuit breaker opens:

- **Closed** (healthy): Deliveries proceed normally
- **Open** (failing): Deliveries are skipped, messages remain in queue
- **Half-open** (recovering): After 5 minutes, one test delivery is attempted

This prevents hammering unhealthy endpoints and wasting resources.

## Monitoring

### Dashboard

The real-time dashboard at http://localhost:5100 shows:

- Delivery statistics (last 24h)
- Endpoint health indicators (Active / Degraded / Failed)
- Message log with filtering (by event type, endpoint, status, date range)
- Delivery attempt details (request headers, response body, latency)
- Live delivery feed via SignalR

### Prometheus Metrics

Scrape `http://localhost:5100/metrics` for detailed delivery metrics. See the [README](../README.md#prometheus-metrics) for the full metrics list.

## Next Steps

- [Self-Hosting Guide](SELF-HOSTING.md) -- production deployment, security, and configuration
- [API Reference](API.md) -- complete endpoint documentation
- [Architecture](ARCHITECTURE.md) -- system design and internals
- [Contributing](../CONTRIBUTING.md) -- how to contribute
