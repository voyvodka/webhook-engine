# Customer Portal Guide

This guide covers the embeddable customer portal shipped in v0.2.0. It is written for two audiences: **SaaS operators** who run WebhookEngine and want to give their customers a self-service endpoint-management UI, and **host SaaS backend engineers** who need to mint portal JWTs and wire up the React component.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Architecture](#2-architecture)
3. [Operator Setup](#3-operator-setup)
4. [Host SaaS Integration](#4-host-sas-integration)
5. [Component Usage (coming soon)](#5-component-usage-coming-soon)
6. [Security Model](#6-security-model)
7. [Configuration Reference](#7-configuration-reference)
8. [Limits](#8-limits)

---

## 1. Overview

The customer portal lets SaaS operators embed a self-service endpoint-management widget directly into their own product's settings UI. Your customers can create, update, test, and delete their webhook endpoints and browse delivery attempt history — without ever touching the WebhookEngine operator dashboard.

**Who it is for:** any SaaS product built on top of WebhookEngine that wants to delegate endpoint management to its end users (tenants / customers). A typical deployment has one WebhookEngine `Application` per customer, and the portal scopes all operations to that application automatically.

**What the engine provides:**

- A narrowed `/api/v1/portal/*` API surface — CRUD on endpoints and event types, endpoint test-fire, attempt history — with admin-only fields (`transformExpression`, `allowedIpsJson`) stripped from reads and writes.
- Per-application HS256 JWT verification middleware — the engine validates tokens but never mints them.
- Per-application dynamic CORS — only origins explicitly approved by the operator are echoed back.
- Full audit logging of every portal mutating action, with the signing key redacted.

**What you build:**

- A short-lived JWT minted by your backend and passed to the frontend component.
- A settings page in your product that renders the `<EndpointManager />` component (shipping in a follow-up tag — see [Section 5](#5-component-usage-coming-soon)).

---

## 2. Architecture

```
Your SaaS Backend                     WebhookEngine
─────────────────                     ─────────────────────────────────────
  Customer settings page
         │
         │  1. Customer loads settings page
         │  2. Frontend requests a portal token
         ▼
  POST /internal/portal-token         (your endpoint — not part of WebhookEngine)
  ├── verify customer session
  ├── look up their WebhookEngine appId
  ├── sign HS256 JWT with PortalSigningKey
  └── return { token, expiresAt }
         │
         │  3. Frontend receives token
         ▼
  <EndpointManager
    apiBase="https://webhooks.yourproduct.com"
    token={portalToken}
    appId={appId}
  />
         │
         │  4. Component calls portal API
         │  Bearer: <token>
         ▼
  GET /api/v1/portal/endpoints        PortalTokenAuthMiddleware
  POST /api/v1/portal/endpoints       ├── validate HS256 signature
  PUT /api/v1/portal/endpoints/{id}   ├── verify algorithm = HS256
  DELETE /api/v1/portal/endpoints/{id}├── check lifetime ≤ 15 min cap
  POST /api/v1/portal/endpoints/{id}/test
  GET /api/v1/portal/endpoints/{id}/attempts
                                      ├── extract appId + capabilities from claims
                                      └── scope all queries to that appId
```

The engine never talks back to your SaaS backend. JWT validation is local — the engine reads the `PortalSigningKey` from the database for the `appId` embedded in the token and verifies the signature in-process.

---

## 3. Operator Setup

### 3.1 Enable the portal for an application

1. Open the WebhookEngine operator dashboard.
2. Navigate to **Applications** and find the application that maps to your customer.
3. Click the row action menu and choose **Portal access**.
4. Click **Enable portal**. The dashboard generates a random 64-character signing key and displays it **once**.
5. Copy the key immediately — it is shown in plaintext exactly once. After you close the modal, the dashboard only shows a masked indicator that the portal is enabled.
6. Store the key as a secret in your SaaS backend (e.g., an environment variable or secrets manager entry keyed to the customer's app ID).
7. Add the allowed CORS origins for your SaaS frontend (e.g., `https://app.yourproduct.com`). Up to 50 origins per application; max 256 characters per origin.

### 3.2 Rotate the signing key

1. Open **Portal access** for the application.
2. Click **Rotate key**. A new key is generated and shown once.
3. Update the key in your SaaS backend before the old tokens expire. The cache invalidation is immediate (within milliseconds) so new tokens signed with the old key will fail as soon as you rotate.

### 3.3 Disable the portal

1. Open **Portal access** for the application.
2. Click **Disable portal**. The signing key is cleared from the database. All in-flight tokens become invalid immediately.

### 3.4 Update allowed CORS origins

In the **Portal access** modal, edit the origin chip list and click **Save**. Changes take effect immediately.

---

## 4. Host SaaS Integration

### 4.1 JWT minting

Your backend is responsible for minting short-lived HS256 JWTs. The engine validates them but never generates them.

**Node.js example using `jose`:**

```js
import { SignJWT } from 'jose'
import { TextEncoder } from 'util'

const PORTAL_SIGNING_KEY = process.env.WEBHOOK_ENGINE_PORTAL_KEY // stored per appId
const APP_ID = 'app_abc123' // the WebhookEngine application ID for this customer

async function mintPortalToken(capabilities = ['endpoints:read', 'endpoints:write', 'endpoints:test', 'attempts:read']) {
  const secret = new TextEncoder().encode(PORTAL_SIGNING_KEY)
  const now = Math.floor(Date.now() / 1000)

  return new SignJWT({
    sub: APP_ID,
    appId: APP_ID,
    cap: capabilities,
  })
    .setProtectedHeader({ alg: 'HS256' })
    .setIssuedAt(now)
    .setNotBefore(now)
    .setExpirationTime(now + 10 * 60) // 10 minutes; engine caps at 15 min
    .sign(secret)
}
```

**Required claims:**

| Claim | Type | Description |
|---|---|---|
| `sub` | string | Must equal the WebhookEngine `appId` |
| `appId` | string | Must equal the WebhookEngine `appId` (duplicate of `sub` for explicitness) |
| `cap` | string[] | One or more capabilities (see below) |
| `iat` | number | Issued-at (Unix epoch seconds) |
| `nbf` | number | Not-before — set to the same value as `iat` |
| `exp` | number | Expiry — must not exceed `iat + MaxLifetimeMinutes * 60` (default 15 min) |

The algorithm header must be `HS256`. Tokens with `alg: none`, `HS384`, or `HS512` are rejected.

### 4.2 Capability list

Capabilities are additive. Include only the ones your customer's tier entitles them to.

| Capability | Unlocks |
|---|---|
| `endpoints:read` | `GET /api/v1/portal/endpoints`, `GET /api/v1/portal/endpoints/{id}` |
| `endpoints:write` | `POST /api/v1/portal/endpoints`, `PUT /api/v1/portal/endpoints/{id}`, `DELETE /api/v1/portal/endpoints/{id}` |
| `endpoints:test` | `POST /api/v1/portal/endpoints/{id}/test` |
| `attempts:read` | `GET /api/v1/portal/endpoints/{id}/attempts` |

Attempting a route without the matching capability returns `403 PORTAL_CAPABILITY_MISSING`.

### 4.3 Token lifetime guidance

Use the shortest lifetime that gives a good user experience:

- **5 minutes** — tight, suitable for high-security contexts. Requires a token refresh mechanism in the component.
- **10 minutes** — good default for most SaaS settings pages.
- **15 minutes** — the engine-enforced maximum (`MaxLifetimeMinutes`). Tokens with a longer `exp` are rejected with `PORTAL_AUTH_LIFETIME_EXCEEDED`.

The engine applies a configurable clock skew tolerance (`ClockSkewSeconds`, default 30 s) when checking `nbf` and `exp`.

### 4.4 Portal API routes

All portal routes are under `/api/v1/portal/` and require `Authorization: Bearer <token>`. The `appId` is read exclusively from the JWT — query/body/route parameters cannot override it.

| Method | Path | Capability | Description |
|---|---|---|---|
| `GET` | `/api/v1/portal/endpoints` | `endpoints:read` | List endpoints for the app |
| `POST` | `/api/v1/portal/endpoints` | `endpoints:write` | Create endpoint (admin-only fields stripped) |
| `GET` | `/api/v1/portal/endpoints/{id}` | `endpoints:read` | Get endpoint |
| `PUT` | `/api/v1/portal/endpoints/{id}` | `endpoints:write` | Update endpoint (admin-only fields stripped) |
| `DELETE` | `/api/v1/portal/endpoints/{id}` | `endpoints:write` | Delete endpoint |
| `POST` | `/api/v1/portal/endpoints/{id}/test` | `endpoints:test` | Send a signed test delivery |
| `GET` | `/api/v1/portal/endpoints/{id}/attempts` | `attempts:read` | List attempt history (paginated) |

Admin-only fields stripped on portal writes and reads: `transformExpression`, `transformEnabled`, `transformValidatedAt`, `allowedIpsJson`.

---

## 5. Component Usage (coming soon)

The `@webhookengine/endpoint-manager` React package is tracked under B1 Step 7 of the roadmap and will ship in a follow-up `portal-v0.1.0` tag after v0.2.0. It is not bundled in the engine release.

When it ships, usage will look roughly like:

```tsx
import { EndpointManager } from '@webhookengine/endpoint-manager'

<EndpointManager
  apiBase="https://webhooks.yourproduct.com"
  token={portalToken}
/>
```

Watch the [GitHub roadmap](https://github.com/voyvodka/webhook-engine/blob/main/docs/ROADMAP.md) for the `portal-v0.1.0` tag.

---

## 6. Security Model

### JWT signing key per application

Each application has an independent 64-character HS256 signing key stored in the `portal_signing_key` column (varchar 64). Rotating or disabling one application's key has no effect on others. The key never appears in API responses, audit log `before_json` / `after_json`, or log output — only a `portalEnabled: true/false` boolean is written to the audit record.

### Algorithm allowlist

`PortalTokenAuthMiddleware` sets `ValidAlgorithms = ["HS256"]` via `Microsoft.IdentityModel.Tokens`. Tokens with any other algorithm (including `alg: none`, `HS384`, `HS512`, any RS/EC variant) are rejected with error code `PORTAL_AUTH_INVALID_SIGNATURE`. The catch-ladder does not echo the rejected algorithm name in the response body.

### Lifetime cap

The `exp` claim is validated against `iat + MaxLifetimeMinutes * 60` (default 15 minutes). Tokens with a longer lifetime are rejected with `PORTAL_AUTH_LIFETIME_EXCEEDED` regardless of the `exp` value the minting side set. This cap is not adjustable by the token itself — it is a server-side policy.

### Dynamic CORS (RFC 6454)

`PortalCorsMiddleware` reads the request `Origin` header, checks it against the application's `AllowedPortalOriginsJson` array using ordinal case-insensitive comparison on the scheme+host+port triple (RFC 6454 compliant), and echoes the validated origin in `Access-Control-Allow-Origin`. Wildcards (`*`) are never emitted. Origins not in the allowlist receive no CORS headers and the preflight returns `403`.

### App-scope isolation

Every portal route extracts `appId` from the validated JWT claims. Route parameters, query strings, and request bodies cannot override or supplement the app scope. A request for an endpoint that belongs to a different application returns `404 PORTAL_NOT_FOUND` — not `403` — so the response shape does not leak the existence of cross-tenant resources.

### Secret entropy floor

The portal `Create` and `Update` endpoint validators require:

- The signing secret to carry the `whsec_` prefix (matching the Standard Webhooks convention).
- The base64-encoded payload after the prefix to be 32–128 characters.

This prevents customers from silently downgrading their HMAC secret to a short or trivially guessable value.

### Signing key rotation flow

1. Operator clicks **Rotate key** in the dashboard.
2. `DashboardPortalController` generates a new key, writes it, and calls `PortalLookupCache.InvalidateApplication(appId)`.
3. The in-memory cache entry is evicted immediately (not waiting for the 60-second TTL).
4. All subsequent portal requests for this app load the new key from the database.
5. Tokens signed with the old key are now invalid. In-flight requests that started before the rotation complete or fail based on whether their token was validated before the cache eviction.

To avoid dropped requests during a rotation: mint a new token with the new key before rotating, then rotate. The component's token refresh cycle should be shorter than the token lifetime.

---

## 7. Configuration Reference

The portal authentication options are under the `WebhookEngine:PortalAuth` configuration section.

| Key | Type | Default | Description |
|---|---|---|---|
| `WebhookEngine:PortalAuth:MaxLifetimeMinutes` | int | `15` | Hard cap on portal JWT lifetime. Tokens whose `exp - iat` exceeds this value are rejected, regardless of what the minting side signed. |
| `WebhookEngine:PortalAuth:ClockSkewSeconds` | int | `30` | Tolerance applied when validating `nbf` and `exp`. Accommodates minor clock drift between the token minter and the engine host. |
| `WebhookEngine:PortalAuth:LookupCacheTtlSeconds` | int | `60` | How long the engine caches a resolved `(appId → signingKey + allowedOrigins)` pair in memory. A rotation calls `InvalidateApplication` to evict immediately, so this TTL only matters for cache warming after a cold start or eviction. |

Environment variable equivalents use double-underscore notation: `WebhookEngine__PortalAuth__MaxLifetimeMinutes=10`.

---

## 8. Limits

| Limit | Value |
|---|---|
| Max allowed CORS origins per application | 50 |
| Max length per origin string | 256 characters |
| Portal signing key length | 64 characters (fixed, generated by the engine) |
| JWT lifetime cap | 15 minutes (configurable down via `MaxLifetimeMinutes`) |
| Capability set (v0.2) | `endpoints:read`, `endpoints:write`, `endpoints:test`, `attempts:read` — fixed; no custom capabilities |
| Attempt history page size | 50 per page (same as the rest of the message/attempt API) |

The capability list is fixed for v0.2. Custom capability scopes and per-endpoint capability restrictions are unscheduled.
