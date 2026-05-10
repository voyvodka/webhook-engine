# ADR-003: Webhook Payload Transformation

**Status:** Accepted
**Date:** 2026-03-30 (proposed) · 2026-05-05 (accepted, all three rollout phases shipped in v0.1.4)
**Context:** Phase 04 Performance & Future Decisions (GHIS-05, GitHub #5)

## Context

WebhookEngine delivers the original message payload to all subscribed endpoints without modification. Some use cases require transforming the payload before delivery:

- **Field filtering:** Remove sensitive fields before sending to external endpoints
- **Schema mapping:** Reshape payload to match consumer's expected schema
- **Enrichment:** Add computed fields (timestamps, hashes, metadata)
- **Format conversion:** Extract nested values and flatten for simpler consumers

Currently, consumers must accept the full payload and transform on their side. This creates coupling between the producer's schema and every consumer's integration code.

### Requirements

- Transformations must be declarative (no arbitrary code execution)
- Transformation failures must not block delivery (fallback to original payload)
- Performance impact must be bounded (timeout, size limits)
- Configuration per endpoint (different endpoints may need different transforms)

## Decision

### Recommended Engine: JMESPath

**Primary recommendation:** JMESPath as the transformation engine.

| Engine | Pros | Cons |
|--------|------|------|
| **JMESPath** | Purpose-built for JSON querying/transformation, well-specified, deterministic, no side effects, .NET library available (JmesPath.Net) | Less widely known than JSONPath, no mutation (select/reshape only) |
| JSONPath | More widely known, familiar to developers | Query-only (no reshape/project), multiple incompatible specs, not suitable for transformation |
| JavaScript sandbox | Maximum flexibility | Security risk, performance unpredictable, requires V8/Jint embedding, complexity |
| Liquid templates | Good for string templating | Not JSON-native, template injection risks, overkill for structured data |

**Why JMESPath over JSONPath:** JSONPath is a query language — it selects values from JSON but cannot reshape the output structure. JMESPath supports projections, multi-select hashes, and nested restructuring, making it a true transformation language. Example:

```
// Input: {"user": {"name": "Alice", "email": "alice@example.com", "ssn": "123-45-6789"}, "event": "order.created"}
// JMESPath: {userName: user.name, userEmail: user.email, eventType: event}
// Output: {"userName": "Alice", "userEmail": "alice@example.com", "eventType": "order.created"}
```

### API Contract

#### Endpoint Configuration

Transformation is configured per endpoint via a new optional field:

```
PUT /api/v1/applications/{appId}/endpoints/{endpointId}
```

Request body addition:
```json
{
  "transformExpression": "{userName: user.name, email: user.email, eventType: event}",
  "transformEnabled": true
}
```

Response includes transformation config:
```json
{
  "id": "...",
  "url": "https://consumer.example.com/webhook",
  "transformExpression": "{userName: user.name, email: user.email, eventType: event}",
  "transformEnabled": true,
  "transformValidatedAt": "2026-03-30T12:00:00Z"
}
```

#### Dashboard Endpoint Configuration

The dashboard endpoint create/update APIs gain the same fields:
```json
{
  "transformExpression": "...",
  "transformEnabled": true
}
```

#### Validation Endpoint (optional, recommended)

```
POST /api/v1/applications/{appId}/endpoints/{endpointId}/transform/validate
```

Request:
```json
{
  "expression": "{userName: user.name}",
  "samplePayload": {"user": {"name": "Test"}}
}
```

Response:
```json
{
  "valid": true,
  "result": {"userName": "Test"},
  "executionTimeMs": 2
}
```

### Data Model Changes

Add to `Endpoint` entity:
- `TransformExpression` (string?, nullable, max 4096 chars) — JMESPath expression
- `TransformEnabled` (bool, default false) — kill switch
- `TransformValidatedAt` (DateTime?, nullable) — last successful validation timestamp

Database columns:
- `transform_expression VARCHAR(4096) NULL`
- `transform_enabled BOOLEAN NOT NULL DEFAULT false`
- `transform_validated_at TIMESTAMPTZ NULL`

### Delivery Pipeline Integration

In `HttpDeliveryService.DeliverAsync()`, after payload retrieval and before HTTP POST:

```
1. Check endpoint.TransformEnabled
2. If true and TransformExpression is not null:
   a. Parse payload as JSON
   b. Apply JMESPath expression with timeout guard
   c. If success: use transformed payload for delivery
   d. If failure: log warning, deliver original payload (fail-open)
3. If false: deliver original payload (current behavior)
```

### Guardrails

| Guardrail | Value | Rationale |
|-----------|-------|-----------|
| Expression max length | 4096 characters | Prevents storage of overly complex expressions |
| Input payload max size | 256 KB (existing limit) | Already enforced in message creation |
| Transformation timeout | 100ms | JMESPath evaluation is fast; 100ms catches pathological expressions |
| Output payload max size | 256 KB | Prevent transformation from inflating payload beyond delivery limits |
| Expression validation | Required on save | Syntactically invalid expressions rejected at configuration time |
| Nested depth limit | 10 levels | Prevent deeply nested projections that may be slow to evaluate |
| Fail-open on error | Always deliver original | Transformation is a convenience, not a gate — delivery must not be blocked |

### Rollout Plan

**Phase 1: Schema + API (v0.3.0)**
- Add database columns via migration
- Add fields to endpoint create/update APIs
- Add validation endpoint
- No delivery pipeline changes — expressions stored but not executed

**Phase 2: Delivery Integration (v0.3.x)**
- Add JmesPath.Net NuGet package
- Integrate transformation into HttpDeliveryService
- Feature flag: `TransformationEnabled` in appsettings.json (global kill switch)
- Log all transformation results (success/failure/fallback) at Info level

**Phase 3: Dashboard UI (v0.4.0)**
- Expression editor with syntax highlighting in endpoint configuration
- Live preview: paste sample payload, see transformed output
- Transformation metrics in dashboard (success rate, avg execution time)

### Migration Path

- **Existing endpoints:** Unaffected — `TransformEnabled` defaults to false, `TransformExpression` defaults to null
- **API compatibility:** New fields are optional in request bodies; responses include new fields with default values
- **Rollback:** Set `TransformationEnabled = false` in appsettings.json to disable globally; individual endpoints can set `TransformEnabled = false`

## Consequences

### Positive
- Endpoints can receive tailored payloads without producer schema changes
- Declarative expressions are safe, auditable, and version-controllable
- Fail-open design preserves delivery reliability
- JMESPath is well-specified with deterministic behavior

### Negative
- Adds complexity to the delivery pipeline (one more step before HTTP POST)
- JMESPath is less familiar than JSONPath to most developers
- Expression debugging requires the validation endpoint or dashboard preview
- New NuGet dependency (JmesPath.Net)

### Neutral
- No impact on existing deployments until expressions are configured
- Transformation metrics add to observability surface (more data to monitor)
- Expression storage increases endpoint row size by up to ~4KB

## Risk Analysis

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| JMESPath expression causes slow evaluation | Low | Medium | 100ms timeout guard; reject known-slow patterns at validation time |
| Transformation changes break consumer integrations | Medium | High | Validation endpoint for pre-flight testing; fail-open preserves original payload |
| JmesPath.Net library abandoned | Low | Medium | JMESPath spec is stable; alternative .NET libraries exist; expressions are engine-agnostic |
| Payload size inflation via transformation | Low | Low | Output size limit (256KB) matches input limit |
| Feature complexity discourages adoption | Medium | Low | Feature is entirely optional; disabled by default |

## Implementation

All three rollout phases shipped together in **v0.1.4** (2026-05-05). The `v0.3.x` / `v0.4.0` placeholders in the original rollout plan were superseded by the actual SemVer cadence.

### Phase 1 — Schema + API (v0.1.4)
- Migration `20260504220757_AddTransformFieldsToEndpoint` adds the three columns.
- `Endpoint` entity carries `TransformExpression`, `TransformEnabled`, `TransformValidatedAt`.
- All four endpoint validators (public Bearer-key + dashboard, create + update) accept and validate the new fields.
- `EndpointResponseDto` exposes them on read.

### Phase 2 — Delivery integration (v0.1.4)
- `JmesPath.Net` 1.1.0 added to `WebhookEngine.Infrastructure`.
- `IPayloadTransformer` interface + `JmesPathPayloadTransformer` implementation.
- `DeliveryWorker` invokes `ApplyTransformation` immediately before signing — the transformed payload is what gets HMAC-signed and POSTed.
- Hard guardrails: 100 ms wall-clock timeout, 256 KB output cap, global kill switch `WebhookEngine:Transformation:Enabled` (default `true`).
- Fail-open behavior: invalid expression / timeout / oversized output / invalid JSON falls back to the original payload with a warning log.
- OpenTelemetry counters: `webhookengine.transformations.applied`, `webhookengine.transformations.failed_open`.
- 6 unit tests cover identity, reshape, invalid expression, empty expression, oversized output, invalid JSON.

### Phase 3 — Dashboard editor (v0.1.4)
- CodeMirror 6 (`@uiw/react-codemirror` + `@codemirror/lang-json`) chosen over Monaco for ~10× smaller bundle (lazy-loaded via `React.lazy()` since v0.1.6 so the chunk only loads when the endpoint editor mounts).
- `<TransformSection />` component: enable toggle + expression editor + collapsible sample-payload playground + Run + result panel.
- `POST /api/v1/dashboard/transform/validate` endpoint (endpoint-agnostic so the editor works during create flows before the row exists). Reuses the same `IPayloadTransformer` that runs at delivery — what passes the editor behaves identically at delivery time.
- 4 integration tests: happy path, invalid expression (200 + `success=false`), empty expression (422), unauthenticated (401).

### Deviations from the original proposal
- **Nested depth limit (10 levels) was not added** — JmesPath.Net does not expose a depth-counting hook, and the 100 ms timeout + 256 KB output cap are sufficient guards in practice. Re-evaluate if a pathological expression survives both.
- **Transformation metrics in the dashboard UI were not added** — the OpenTelemetry counters are emitted but not surfaced in any dashboard panel yet. Tracked as opt-in follow-up; will land when there is real demand.
- **`TransformValidatedAt` is server-managed but not yet shown in the dashboard.** Field is populated on successful validate-endpoint call; surfacing it in the editor is a small UX polish, not a behavior change.
