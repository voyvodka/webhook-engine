# Changelog — @webhookengine/endpoint-manager

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The package versions independently from the WebhookEngine engine. The npm
release tag scheme is `portal-v{major}.{minor}.{patch}` (engine releases use
`v{major}.{minor}.{patch}`).

## [Unreleased]

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
