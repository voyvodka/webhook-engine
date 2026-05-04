# Product Requirements Document (PRD)
# WebhookEngine — Open-Source Webhook Delivery Infrastructure

**Version:** 1.0
**Last Updated:** 2026-03-02
**Status:** Active — Phase 2 (Traction & Feedback)

---

## 1. Executive Summary

WebhookEngine is a self-hosted, open-source webhook delivery platform built with ASP.NET Core, React, and PostgreSQL. It provides reliable webhook sending with automatic retries, cryptographic signing, delivery monitoring, and a real-time dashboard.

**Target users:** SaaS companies and development teams that need to send webhooks to their customers/partners reliably.

**Core value proposition:** "Never lose a webhook again. Self-hosted. MIT licensed. PostgreSQL-native."

**Position in market:** The .NET ecosystem's answer to Svix (Rust) and Convoy (Go). The only open-source, PostgreSQL-native, easy-to-self-host webhook infrastructure.

---

## 2. Problem Statement

### 2.1 Why Webhooks Matter
Webhooks are the backbone of modern SaaS integration. When an event happens in your application (order created, payment received, user signed up), downstream systems need to know. Webhooks deliver this information by making HTTP POST requests to registered endpoints.

### 2.2 The Pain
Every team building webhook delivery faces the same problems:

**P1 — Reliability:**
Endpoints go down. Networks timeout. DNS fails. Without proper retry logic, exponential backoff, and failure tracking, webhooks silently disappear. The customer's integration breaks, and nobody knows until they complain.

**P2 — Visibility:**
"Did our webhook reach the customer?" Most teams have zero visibility into webhook delivery. Debugging means grepping application logs. There's no dashboard, no delivery history, no way to replay failed events.

**P3 — Security:**
HMAC signature verification, timestamp-based replay attack prevention, IP allowlisting — these are table stakes for production webhooks. Most teams implement them partially or not at all.

**P4 — Operational Overhead:**
Every SaaS team that sends webhooks reimplements the same infrastructure: queue, worker, retry logic, logging, monitoring. This takes 2-6 weeks of engineering time per project, repeated endlessly.

**P5 — Self-Hosting Requirement:**
Regulated industries (fintech, healthtech, government) and GDPR/KVKK-conscious companies need webhook infrastructure that runs in their own environment. Svix charges $490/month for Pro. Convoy uses a restrictive license. There's no truly free, self-hostable option with production features.

### 2.3 Who Has This Problem
- SaaS companies with public APIs (Stripe-like webhook notifications)
- B2B platforms notifying partner integrations
- Microservice architectures using webhooks for service-to-service communication
- E-commerce platforms sending order/payment notifications
- Any application that needs to notify external systems about events

---

## 3. Goals & Non-Goals

### 3.1 Goals
- **G1:** Provide reliable, at-least-once webhook delivery with configurable retry policies
- **G2:** Offer full delivery visibility through a real-time React dashboard
- **G3:** Implement webhook security best practices (HMAC signing, replay prevention) out of the box
- **G4:** Enable single-command self-hosting via Docker Compose with PostgreSQL (no MongoDB, no Redis required for MVP)
- **G5:** Deliver a clean REST API that any language/framework can consume
- **G6:** Provide native .NET SDK (NuGet) and TypeScript SDK (npm) for easy integration
- **G7:** Support multi-application tenancy (one WebhookEngine instance serves multiple apps/customers)
- **G8:** Open-source core under MIT license

### 3.2 Non-Goals (explicitly out of scope for MVP)
- Incoming webhook gateway (receiving webhooks from third parties)
- Webhook payload transformation/mapping
- GraphQL API (REST only for MVP)
- Kafka/RabbitMQ as queue backend (PostgreSQL-based queue for MVP)
- Email/SMS/push notification channels (that's the Notification Infrastructure project)
- Mobile SDK
- Kubernetes Helm charts (Docker Compose only for MVP)

---

## 4. User Personas

### Persona 1: Backend Developer ("Builder")
- Integrating WebhookEngine into their SaaS application
- Cares about: clean API, good SDK, easy Docker setup, clear documentation
- Uses: REST API, NuGet/npm SDK, Docker Compose
- Pain today: writing retry logic from scratch, no visibility into delivery failures

### Persona 2: DevOps / Platform Engineer ("Operator")
- Deploying and maintaining WebhookEngine
- Cares about: easy deployment, monitoring, PostgreSQL compatibility, resource usage
- Uses: Docker Compose, health endpoints, Prometheus metrics
- Pain today: managing separate queue infrastructure (RabbitMQ/Redis) just for webhooks

### Persona 3: API Consumer ("Receiver")
- External developer receiving webhooks from a SaaS that uses WebhookEngine
- Cares about: reliable delivery, signature verification, delivery logs, ability to test endpoints
- Uses: Endpoint management portal (if exposed), webhook signature verification docs
- Pain today: missed webhooks, no way to replay, no delivery history

### Persona 4: Product/Engineering Lead ("Decision Maker")
- Evaluating webhook infrastructure options
- Cares about: self-hosting capability, license, cost, security, compliance (GDPR/KVKK)
- Compares: build in-house vs Svix vs Convoy vs WebhookEngine
- Pain today: Svix is $490/mo, Convoy's license is restrictive, building in-house takes weeks

---

## 5. Functional Requirements

### 5.1 Application Management

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-01 | Create/update/delete applications (logical tenants within WebhookEngine) | P0 |
| FR-02 | Each application has its own API key for authentication | P0 |
| FR-03 | Applications are fully isolated (endpoints, events, logs) | P0 |
| FR-04 | Application-level settings (retry policy, signing secret rotation) | P1 |

### 5.2 Event Types

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-10 | Define event types per application (e.g., `order.created`, `payment.failed`) | P0 |
| FR-11 | Event types have name, description, and optional JSON schema | P1 |
| FR-12 | Event types can be archived (soft delete) | P2 |

### 5.3 Endpoint Management

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-20 | Register webhook endpoints (URL + optional description) | P0 |
| FR-21 | Subscribe endpoints to specific event types (filter which events they receive) | P0 |
| FR-22 | Enable/disable endpoints without deleting | P0 |
| FR-23 | Endpoint health status: Active, Degraded (failing intermittently), Failed (circuit open) | P0 |
| FR-24 | Endpoint-level secret override (different signing secret per endpoint) | P1 |
| FR-25 | Endpoint metadata (key-value pairs for customer reference) | P2 |
| FR-26 | Custom headers per endpoint (e.g., Authorization header) | P1 |

### 5.4 Message Sending

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-30 | Send a message by specifying: application, event type, payload (JSON) | P0 |
| FR-31 | Message is fan-out delivered to all endpoints subscribed to that event type | P0 |
| FR-32 | Each endpoint delivery is tracked as a separate "message attempt" | P0 |
| FR-33 | Support idempotency key to prevent duplicate sends | P1 |
| FR-34 | Support batch message sending (multiple events in one API call) | P2 |

### 5.5 Delivery & Retry Engine

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-40 | At-least-once delivery guarantee | P0 |
| FR-41 | Configurable retry schedule with exponential backoff (default: 5s, 30s, 2m, 15m, 1h, 6h, 24h) | P0 |
| FR-42 | Maximum retry count configurable per application (default: 7) | P0 |
| FR-43 | Circuit breaker per endpoint: after N consecutive failures, pause delivery and mark endpoint as Failed | P0 |
| FR-44 | Dead letter queue: permanently failed messages are stored and queryable | P0 |
| FR-45 | Manual retry: re-attempt delivery of a specific failed message via API or dashboard | P0 |
| FR-46 | Delivery timeout configurable (default: 30 seconds) | P1 |
| FR-47 | Success criteria: HTTP 2xx response. Anything else (4xx, 5xx, timeout) = failure | P0 |
| FR-48 | Rate limiting per endpoint (e.g., max 100 deliveries/second) | P2 |

### 5.6 Security

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-50 | HMAC-SHA256 signature on every delivery in `webhook-signature` header | P0 |
| FR-51 | Timestamp included in signature to prevent replay attacks (`webhook-timestamp` header) | P0 |
| FR-52 | Unique message ID in `webhook-id` header | P0 |
| FR-53 | Signing secret rotation: generate new secret while old one remains valid for N hours | P1 |
| FR-54 | API authentication via API key (Bearer token) | P0 |
| FR-55 | API key rotation without downtime | P1 |
| FR-56 | Dashboard authentication (built-in login or OAuth integration) | P0 |

### 5.7 Dashboard (React)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-60 | Application overview: list of apps with endpoint counts and health summary | P0 |
| FR-61 | Endpoint list with health status indicators (green/yellow/red) | P0 |
| FR-62 | Message/delivery log: filterable by event type, endpoint, status, date range | P0 |
| FR-63 | Delivery attempt detail: request headers, request body, response status, response body, latency | P0 |
| FR-64 | Manual retry button on failed deliveries | P0 |
| FR-65 | Endpoint create/edit/disable/delete from UI | P0 |
| FR-66 | Event type management from UI | P1 |
| FR-67 | Real-time delivery feed (live updates as deliveries happen) | P1 |
| FR-68 | Delivery statistics: success rate, average latency, failure breakdown (charts) | P1 |
| FR-69 | Dark mode | P2 |

### 5.8 Observability

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-70 | Health check endpoint (`/health`) for load balancers | P0 |
| FR-71 | Structured logging (Serilog with JSON output) | P0 |
| FR-72 | Prometheus metrics endpoint (`/metrics`): delivery count, success/fail rate, latency histogram, queue depth | P1 |
| FR-73 | OpenTelemetry trace propagation on outgoing webhook requests | P2 |

---

## 6. Non-Functional Requirements

| ID | Requirement | Target |
|----|-------------|--------|
| NFR-01 | Delivery latency (time from API call to first attempt) | < 500ms (p95) |
| NFR-02 | Dashboard page load time | < 2 seconds |
| NFR-03 | API response time (send message) | < 200ms (p95) |
| NFR-04 | Concurrent delivery throughput (single instance) | 100+ deliveries/second |
| NFR-05 | PostgreSQL as only external dependency | Required |
| NFR-06 | Docker Compose single-command deployment | Required |
| NFR-07 | Memory usage (idle, single instance) | < 256MB |
| NFR-08 | Zero data loss on graceful shutdown | Required |
| NFR-09 | Database migration on startup (automatic) | Required |
| NFR-10 | Backward-compatible API versioning | v1 prefix |

---

## 7. Webhook Delivery Protocol

This section defines the HTTP request format that WebhookEngine sends to registered endpoints. This is the "contract" that webhook consumers implement against.

### 7.1 Request Format

```http
POST {endpoint_url} HTTP/1.1
Content-Type: application/json
User-Agent: WebhookEngine/1.0
webhook-id: msg_2xHg7kLMNpQ9rSt
webhook-timestamp: 1740600000
webhook-signature: v1,K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols=

{
  "type": "order.created",
  "timestamp": "2026-02-26T14:30:00Z",
  "data": {
    "order_id": "ord_abc123",
    "amount": 99.99,
    "currency": "TRY"
  }
}
```

### 7.2 Signature Verification (Consumer Side)

```
signature_payload = "{webhook_id}.{webhook_timestamp}.{body}"
expected_signature = Base64(HMAC-SHA256(signing_secret, signature_payload))
```

Consumer verifies:
1. Compute expected signature from payload
2. Compare with `webhook-signature` header value (after `v1,` prefix)
3. Reject if timestamp is older than 5 minutes (replay attack prevention)

### 7.3 Response Expectations
- **2xx** → Success. Message marked as delivered.
- **4xx** → Client error. Retried (the consumer's endpoint may be temporarily misconfigured).
- **5xx** → Server error. Retried.
- **Timeout (>30s)** → Retried.
- **Connection refused** → Retried.

### 7.4 Retry Schedule (Default)

| Attempt | Delay After Previous | Cumulative Time |
|---------|---------------------|-----------------|
| 1 | Immediate | 0s |
| 2 | 5 seconds | 5s |
| 3 | 30 seconds | 35s |
| 4 | 2 minutes | ~2.5m |
| 5 | 15 minutes | ~17.5m |
| 6 | 1 hour | ~1h 17m |
| 7 | 6 hours | ~7h 17m |
| 8 | 24 hours | ~31h 17m |

After all retries exhausted → message moves to Dead Letter Queue.

---

## 8. Success Metrics

### 8.1 Product Metrics
| Metric | 3 Month Target | 6 Month Target | 12 Month Target |
|--------|---------------|----------------|-----------------|
| GitHub stars | 200 | 1,000 | 3,000 |
| Docker Hub pulls | 500 | 5,000 | 20,000 |
| NuGet downloads (SDK) | 100 | 2,000 | 10,000 |
| Contributors | 1 (self) | 5 | 15 |
| Production deployments (estimated) | 5 | 50 | 200 |

### 8.2 Technical Metrics
| Metric | Target |
|--------|--------|
| Delivery success rate (non-dead endpoints) | > 99.5% |
| p95 delivery latency (first attempt) | < 500ms |
| Uptime (self-hosted reference) | > 99.9% |
| Test coverage | > 80% |

---

## 9. Open Questions

| # | Question | Status |
|---|----------|--------|
| Q1 | Should the dashboard be a separate deployable or bundled into the main app? | **Decision: Bundled as SPA served by ASP.NET Core for simplicity** |
| Q2 | PostgreSQL-based queue vs. dedicated queue (Redis/RabbitMQ) for MVP? | **Decision: PostgreSQL-based for MVP (zero extra dependencies). Pluggable queue interface for future.** |
| Q3 | Should we support webhook receiving (incoming) in MVP? | **Decision: No. Outgoing only for MVP. Incoming is post-MVP.** |
| Q4 | Multi-region delivery? | Out of scope for MVP and v1. |
| Q5 | Project name — "WebhookEngine" is placeholder. Final name TBD. | **Decision: Final name is WebhookEngine** |
| Q6 | Svix-compatible API format for easy migration? | Worth investigating post-MVP |

---

## 10. References

- [Svix Documentation](https://docs.svix.com/) — API design reference
- [Convoy Documentation](https://docs.getconvoy.io/) — Architecture reference
- [Standard Webhooks Specification](https://www.standardwebhooks.com/) — Signing format standard
- `open-source-ideas/02-webhook-delivery.md` — Original idea document with competitive analysis
