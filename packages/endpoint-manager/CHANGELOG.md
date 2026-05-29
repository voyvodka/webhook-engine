# Changelog — @webhookengine/endpoint-manager

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The package versions independently from the WebhookEngine engine. The npm
release tag scheme is `portal-v{major}.{minor}.{patch}` (engine releases use
`v{major}.{minor}.{patch}`).

## [Unreleased]

Contract-alignment fix: the `0.1.0` client diverged from the live engine and the
divergence was masked because the client tests and the sample mocked an idealized
shape rather than the engine's real DTOs. The next release is strongly recommended
over `0.1.0` — `0.1.0` endpoint updates fail against a real engine and editing
wipes custom headers.

### Fixed
- **`updateEndpoint()` now issues `PATCH`, not `PUT`.** The engine route is `[HttpPatch]`; the `0.1.0` `PUT` did not match and every endpoint update from the embedded component failed against a real engine.
- **Endpoint status is read from `status`, not `isActive`.** The engine returns a lowercased `EndpointStatus` string (`active`/`degraded`/`failed`/`disabled`); `0.1.0` read a non-existent `isActive` boolean, so the status badge always showed "Disabled". `<EndpointList />` now renders all four states.
- **Editing an endpoint no longer wipes its custom headers.** The engine returns header NAMES only (values are never exposed to the portal). `0.1.0` read a non-existent `customHeaders` map, started the editor empty, and sent `customHeaders: {}` on save — silently clearing all headers. The editor now shows existing header names read-only and only sends `customHeaders` when the operator enters new ones (an empty set preserves the stored headers).

### Changed
- **Types now mirror the engine DTOs:** `PortalEndpointSummary`/`PortalEndpointDetail` expose `status: PortalEndpointStatus` and `customHeaderNames: string[]` (was `isActive` / `customHeaders`). New `PortalEndpointStatus` union exported.
- **Attempt status badge** handles the real `AttemptStatus` values (`success`/`failed`/`timeout`/`sending`) instead of a `success`/`failure` binary.
- **Secret-override validation message** clarified to match the actual check (≥32 chars after `whsec_`).

### Tests
- Client and component test fixtures now use the real engine response shape, plus a dedicated contract test that asserts `updateEndpoint` issues `PATCH` and that `status` + `customHeaderNames` deserialize correctly (with unknown server fields ignored). The `samples/portal-host` mock now mirrors the real contract (PATCH, `status`, `customHeaderNames`, event-type IDs).

## [0.1.0] - 2026-05-11

Initial public release. Pairs with WebhookEngine engine v0.2.0 or later (which exposes the `/api/v1/portal/*` route group the package consumes).

### Added
- **`<EndpointManager />`** — the headline embeddable React component. Wraps a self-contained portal that authenticates against a host SaaS-minted HS256 JWT and serves a customer-facing endpoint management UI. Props: `baseUrl`, `token`, `appId`, `capabilities`, `theme`, `className`, `onError`, `onUnauthorized`.
- **`<EndpointList />`** — paginated table of endpoints with status badges, capability-gated `[+ New endpoint]` + per-row Edit/Enable/Disable/Delete/Test/Attempts actions.
- **`<EndpointEditor />`** — modal-style overlay for create + edit. Fields: URL (HTTPS), description, custom headers (key/value editor), event-type filter (multi-select), secret override (`whsec_` prefix + 32+ char client-side check). Server validation `fieldErrors` route to per-field inline messages. Field narrowing enforced at the DTO level — `transformExpression` / `transformEnabled` / `allowedIpsJson` are dropped on write.
- **`<EndpointTester />`** — modal opened from the Test row action. JSON payload validation on blur, color-coded response panel (status + latency + body, collapsed if >500 chars), collapsible signed-request preview showing the URL + headers (`webhook-id` / `webhook-timestamp` / `webhook-signature`) + body the receiver actually HMAC-verifies.
- **`<AttemptList />`** — modal opened from the Attempts row action. Paginated delivery history with relative + absolute timestamps, success / failure status badges, HTTP code, latency, expandable response excerpts.
- **`createPortalClient()`** — fetch wrapper, **zero runtime dependencies**. `Bearer` auth, `ApiEnvelope` unwrap, 4xx/5xx → `PortalError` with `code` + `status` + `fieldErrors`, 401 hooks `onUnauthorized` for token re-mint flows. Methods: `listEndpoints`, `getEndpoint`, `createEndpoint`, `updateEndpoint`, `deleteEndpoint`, `enableEndpoint`, `disableEndpoint`, `testEndpoint`, `listAttempts`, `listEventTypes`.
- **`PortalCapability` union and full TypeScript types** for `PortalAppState`, `PortalEndpointSummary`, `PortalEndpointDetail`, `PortalAttempt`, `PortalTestResult`, `PortalListResult<T>`, `PortalError`, `PortalClientOptions`, `EndpointManagerProps`. Re-exported from `./types`.
- **Tailwind 4 internal compile pipeline.** `dist/style.css` ships pre-compiled with the package. The `@theme` block defines `--color-whe-*` tokens (background, text, border, accent, success, danger, warning) — consumers override at `:root` or `.whe-portal` scope to re-theme without touching component code. The `.whe-portal` wrapper className is the load-bearing scoping mechanism so consumer page styles can't leak in and our utilities can't leak out.
- **42-test vitest suite** (vitest + happy-dom + `@testing-library/react`) covering the client contract, capability gating, field-narrowing, JSON validation, secret-override entropy floor, signed-request preview, status badge color-coding, and pagination boundaries.
- **`samples/portal-host/` reference app** in the engine repo demonstrating consumer integration: Vite + mocked fetch + browser-side JWT mint (`mint-token.ts`) — for local exploration only. Production token minting belongs on the host SaaS's own backend.

### Notes
- **Bundle:** ESM-only, ~14.2 KB gzip JS + ~4.3 KB gzip CSS. Tree-shakable.
- **Peer dependencies:** `react ^19.0.0`, `react-dom ^19.0.0`. Zero runtime dependencies.
- **Engine compatibility:** v0.2.0 or later. Earlier engines lack the `/api/v1/portal/*` surface.
- **JWT requirements:** HS256, signed with the per-app `PortalSigningKey` (rotated from the engine's operator dashboard). Lifetime cap defaults to 15 min on the engine side. The component never sees the signing key — only the bearer token.
