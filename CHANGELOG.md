# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.5] - 2026-05-05

This release is the **post-audit hardening cut**. A multi-agent deep audit covered security, memory, concurrency, code quality, frontend, operations, timezone correctness, and NuGet SDK compliance; the ten resulting fixes (F1–F10) plus an idempotency race fix (F7) and an SDK target-framework simplification all land here. No breaking API changes — the `v1` route prefix and Standard Webhooks header names are preserved.

### Added
- **Liveness / readiness health probes (audit Ops).** `HealthController` now serves `/health/live` (process is up) and `/health/ready` (`AppReadinessGate` + `DbContext.CanConnectAsync`) alongside the original `/health`. `/health/ready` returns `503` with a reason while migrations or DI are still warming up so orchestrators (Kubernetes, Compose health checks) can wait correctly.
- **OpenTelemetry tracing with optional OTLP export (audit Ops).** `AddOpenTelemetry().WithTracing(...)` instruments ASP.NET Core; an OTLP exporter activates when `OpenTelemetry:OtlpEndpoint` is set, so deployments can pipe traces into Tempo / Honeycomb / Grafana Cloud without code changes.
- **`WebhookVerifier` in the .NET SDK (audit SDK).** Standard Webhooks signature verification ships in `WebhookEngine.Sdk` for the first time: 5-minute default tolerance, `CryptographicOperations.FixedTimeEquals` constant-time comparison, support for `whsec_` prefix and base64 secrets, and multi-value signatures for secret rotation. Receivers no longer need to roll their own verifier.

### Changed
- **SDK target framework simplified to `.NET 10` only.** `WebhookEngine.Sdk` previously multi-targeted `net8.0`, `net9.0`, and `net10.0`. The package now ships a single `net10.0` target, matching the rest of the project's stack lock. This is a pre-`v1.0` cleanup — the package has effectively zero installed user base, and self-hosted webhook deployments are overwhelmingly on `.NET 10` already. Side effects: NuGet badge flips from `.NET 8.0` to `.NET 10.0`, build cost drops 3×, and modern BCL APIs (`System.Threading.Lock`, `FrozenDictionary`, source-generated JSON, `OrderedDictionary<,>`) are unblocked for future SDK work.
- **`IMemoryCache` is bounded and per-app invalidatable (audit Memory C1 + C2).** The lookup cache is now configured with `SizeLimit = 10_000` and every entry sets `Size = 1`, so the cache cannot grow without bound under churn. `DeliveryLookupCache.InvalidateApplication(appId)` cancels the per-app `CancellationTokenSource` change-token so endpoint / event-type mutations evict stale entries immediately.
- **Graceful shutdown drain window (audit Ops H2).** `HostOptions.ShutdownTimeout = 45s` so in-flight HTTP deliveries can complete cleanly when the process receives SIGTERM, instead of being torn down at the default 5-second ceiling.
- **Dashboard admin default credentials are rejected outside Development.** `DashboardAdminSeeder` throws on startup if the seeded email is `admin@example.com` or the password is `admin`, `changeme`, `password`, or under 12 characters in non-Development environments. Operators must set real credentials before the API will start.

### Fixed
- **Idempotency race condition closed (audit Concurrency H1, F7).** Two concurrent requests with the same `idempotencyKey` previously slipped past the time-window pre-check and double-enqueued the message — both threads saw an empty lookup and both inserted. A new partial unique index on `(app_id, endpoint_id, idempotency_key) WHERE idempotency_key IS NOT NULL` now serializes inserts at the database, and the controllers (`POST /api/v1/messages`, `POST /api/v1/messages/batch`, `POST /api/v1/dashboard/messages`) catch the resulting `23505` conflict to perform a Stripe-style replay: fetch the winning row for that `(app, endpoint, key)` triple and return its id as if it had been freshly enqueued. The window-based reuse semantics are preserved — `RetentionCleanupWorker` now NULL-outs `idempotency_key` on rows past the per-app `IdempotencyWindowMinutes` so the same key can be re-used in a fresh window without violating the index. The companion non-unique index `idx_messages_app_idempotency` is kept for the lookup path. Migration `20260505140607_AddIdempotencyUniqueIndex` is defensive: it NULL-outs any pre-existing duplicate triples (keeping the most-recent row per group) before creating the unique index, so the migration cannot fail on legacy data.
- **Duplicate-attempt regression on lock loss closed (audit Concurrency C2, F2).** `MessageRepository.MarkDeliveredAsync` / `MarkFailedForRetryAsync` / `MarkDeadLetterAsync` are now compare-and-set (`WHERE Id = @id AND Status = Sending AND LockedBy = @lockedBy`) and return `bool`. `DeliveryWorker` checks the result and abandons silently when the row was stolen by stale-lock recovery, eliminating the previous duplicate `MessageAttempt` insertion path.
- **`EndpointHealthTracker` race serialized via PostgreSQL advisory lock (audit Concurrency C1, F3).** Circuit-breaker mutations on the same endpoint could interleave between concurrent workers, corrupting `ConsecutiveFailures` and the Open / HalfOpen / Closed state. `WithEndpointLockAsync` now wraps every mutation in a transaction with `pg_advisory_xact_lock(((100_001L << 32) | endpointHash))`, re-reads under lock, mutates, and commits. Behavior is unchanged on the InMemory test provider.
- **Frontend session-expiry redirect and chunk-load recovery (audit Frontend C1 + H4, F8).** A `webhookengine:auth-expired` `CustomEvent` fires on any 401 outside `/api/v1/auth/*`; `AuthContext` listens, clears user state, and the router redirects to login. A new `RouteErrorBoundary` catches `ChunkLoadError` (e.g., after a deploy invalidates cached lazy chunks) and renders an "Update available — Reload" prompt instead of crashing the SPA.
- **`HttpResponseMessage` disposal and rate-limiter eviction (audit Memory H1 + H2, F9).** `HttpDeliveryService` now uses `using var` on both the `HttpRequestMessage` and `HttpResponseMessage` so socket-pool entries don't leak under high failure rates. `EndpointRateLimiter` evicts inactive endpoint windows after 15 minutes (5-minute sweep) so the dictionary cannot grow unbounded across an endpoint's lifetime.
- **Migration startup race serialized via advisory lock (audit Ops, F5).** Concurrent API replicas could race the migrator at startup; the migration block in `Program.cs` now wraps `Database.MigrateAsync()` in `pg_advisory_lock(((200_000L << 32) | 1))` so only one replica runs the migrator while the others wait.

### Security
- **SSRF guard with DNS-rebinding defense (audit Security C1, F1).** Endpoint URL validation now performs a DNS resolve and rejects any address that maps to RFC1918, loopback, link-local, CGNAT, multicast, reserved, or IPv6 unique-local / link-local / IPv4-mapped private ranges. The `webhook-delivery` `HttpClient` uses a `SocketsHttpHandler.ConnectCallback` that pins the resolved IP for the lifetime of the request, so a malicious DNS server cannot return a public IP at validate-time and a private IP at connect-time. A new `WebhookEngine:SsrfGuard` options section (`Enabled`, `AllowLoopbackInDevelopment`) gates the policy.
- **Security headers, `/metrics` auth gate, and cookie hardening (audit Security H1 + H3 + Medium 2, F6).** `SecurityHeadersMiddleware` now sets `Strict-Transport-Security` (in non-Dev), `Content-Security-Policy`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, and `Permissions-Policy`. `MetricsAuthMiddleware` requires `Authorization: Bearer <token>` on `/metrics` when `WebhookEngine:Metrics:ScrapeToken` is configured. The dashboard cookie now uses `SecurePolicy = Always` outside Development and Testing, so an HTTPS-only deployment cannot accidentally issue cookies over plaintext.
- **Custom-header allow-list and reserved-header rejection (audit Security M3, F10).** `CustomHeaderPolicy` rejects per-endpoint custom headers that would override engine-set headers (`Authorization`, `Cookie`, `Set-Cookie`, `Host`, `Content-*`, `Transfer-Encoding`, `User-Agent`, `webhook-id`, `webhook-timestamp`, `webhook-signature`), strips CR/LF, and enforces size bounds (name ≤128, value ≤1024). Applied to all four endpoint validators (public + dashboard, create + update).

## [0.1.4] - 2026-05-05

### Fixed
- **Multi-architecture Docker image:** the published image now ships for both `linux/amd64` and `linux/arm64`. Previous releases were amd64-only, which meant Apple Silicon Macs and arm64 Linux servers got `no matching manifest for linux/arm64/v8` when running `docker pull voyvodka/webhook-engine`. The release workflow gains a QEMU setup step and the build action now passes `platforms: linux/amd64,linux/arm64`.
- **Removed phantom "unknown / unknown" tag entry on Docker Hub:** `docker/build-push-action`'s default provenance + SBOM attestations were landing on Docker Hub as a separate "unknown / unknown" platform row alongside the real architectures. `provenance: false` and `sbom: false` are now set explicitly so each tag lists only the platforms it actually contains.

### Security
- **Docker base image refresh (Docker Scout cleanup):** all three Dockerfile stages (`oven/bun:1-alpine`, `mcr.microsoft.com/dotnet/sdk:10.0`, `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`) now ship with SHA-256 digest pins so Dependabot can track and bump them, and the release workflow forces `pull: true` to bypass the GitHub Actions build cache when fetching upstream layers. The published image picks up the latest Alpine 3.23.4 patches: openssl/libcrypto3/libssl3 `3.5.5-r0` → `3.5.6-r0` (1 critical + 5 high CVEs cleared) and musl `1.2.5-r21` → `1.2.5-r23` (1 high CVE cleared). The remaining busybox advisory has no upstream patch yet and persists across the Alpine ecosystem.

### Added
- **Brand icon on the NuGet package + Docker Hub description sync:** the `WebhookEngine.Sdk` NuGet package now ships with a 256×256 brand icon (the same diamond + three-dots mark used on the landing page and dashboard), so the package shows the project logo on nuget.org instead of the default placeholder. The release workflow also runs `peter-evans/dockerhub-description` after pushing the Docker image, syncing the GitHub README into the Docker Hub repository's "Overview" tab on every release. Image labels gained `org.opencontainers.image.documentation` and `org.opencontainers.image.vendor`.
- **OpenAPI document + Scalar interactive reference:** the API host now generates an OpenAPI 3 document via `Microsoft.AspNetCore.OpenApi` (.NET 10 native) and serves an interactive [Scalar](https://scalar.com/) UI alongside it. Routes — `/openapi/v1.json` (spec) and `/scalar` (UI) — are mapped only in `Development` and `Staging` environments; `Production` deployments leave the surface unmapped. The document covers all 39 controller routes and is suitable for SDK auto-generation.
- **Payload transformation dashboard editor (ADR-003 Phase 3):** the endpoint create/edit dialog now hosts a `<TransformSection />` with a CodeMirror 6-powered JMESPath expression editor and a collapsible playground. Users can paste a sample JSON payload, click **Run**, and see either the transformed output or the parser error inline. A new `POST /api/v1/dashboard/transform/validate` endpoint reuses the same `IPayloadTransformer` (and its timeout/size guards) that runs in the delivery pipeline, so what passes the editor will behave identically at delivery time. The endpoint is intentionally endpoint-agnostic — it can be used during create flows before the row exists. CodeMirror 6 was chosen over Monaco for a much smaller bundle (~150 KB gzip vs ~1–2 MB).
- **Payload transformation delivery integration (ADR-003 Phase 2):** the `DeliveryWorker` now applies the per-endpoint JMESPath expression to the payload before signing and POSTing. Backed by the new `IPayloadTransformer` abstraction and `JmesPathPayloadTransformer` (JmesPath.Net 1.1.0). Hard guardrails enforced at delivery time: 100 ms wall-clock timeout, 256 KB output cap, and a global kill switch via `WebhookEngine:Transformation:Enabled` (defaults to `true`). Every transformation is fail-open — invalid expressions, timeouts, oversized output, or invalid JSON fall back to the original payload with a warning log. New OpenTelemetry counters `webhookengine.transformations.applied` and `webhookengine.transformations.failed_open` track success vs fallback. Six unit tests cover identity, reshape, invalid expression, empty expression, output-size, and invalid-json paths.
- **Payload transformation schema and API (ADR-003 Phase 1):** endpoints now accept `transformExpression` (JMESPath, max 4096 chars), `transformEnabled` (kill switch, default `false`), and a server-managed `transformValidatedAt` timestamp on create/update. Both the public Bearer-key API (`POST /api/v1/endpoints`, `PUT /api/v1/endpoints/{id}`) and the dashboard endpoints (`POST /api/v1/dashboard/endpoints`, `PUT /api/v1/dashboard/endpoints/{id}`) carry the new fields, and `EndpointResponseDto` exposes them on read. The dashboard expression editor and live preview land in ADR-003 Phase 3.
- **Security automations:** CodeQL workflow (csharp + javascript-typescript, push/PR/Mondays at 06:30 UTC), Dependency Review action on PRs (high-severity fail + GPL/LGPL/AGPL/EUPL/SSPL deny-list), and Dependabot config covering NuGet, npm, GitHub Actions, and Docker base images. Five repo labels (`dependencies`, `nuget`, `npm`, `ci`, `docker`) created to support the Dependabot config.

### Changed
- **Frontend toolchain:** migrated dashboard package manager from Yarn to [Bun](https://bun.sh/) 1.2+. `yarn.lock` replaced with `bun.lock` (text format introduced in Bun 1.2); CI, Dockerfile, contributor docs, and PR template now reference `bun` commands. No runtime behavior changes.

### Removed
- **`WebhookEngine.Application` project:** removed from the solution along with its `WebhookEngine.Application.Tests` companion. The project had been empty since the CQRS scaffold removal in v0.1.0 (see ADR-002). Solution entries, the API project's `ProjectReference`, the Dockerfile `COPY` line, and documentation references have all been cleaned up. ADR-002 was updated with the revised decision. No runtime behavior changes — the project never contained executable code.

## [0.1.3] - 2026-04-08

### Added
- Landing page at [webhook.sametozkan.com.tr](https://webhook.sametozkan.com.tr) — project overview, features, quick start, and links to GitHub, Docker Hub, NuGet, and docs.

### Changed
- `README.md`: added website, Docker Hub, and NuGet links to the header.
- `WebhookEngine.Sdk`: version aligned with main project (`0.1.3`); `PackageProjectUrl` updated to the landing page.

## [0.1.0] - 2026-03-02

### Added
- Sample applications for end-to-end webhook flow:
  - `samples/WebhookEngine.Sample.Sender` (SDK-based sender)
  - `samples/WebhookEngine.Sample.Receiver` (signature-verifying receiver)
- Signature verification helpers in C#, TypeScript, and Python under `samples/signature-verification/`.
- New guides:
  - `docs/GETTING-STARTED.md`
  - `docs/SELF-HOSTING.md`
- Contribution and collaboration files:
  - `CONTRIBUTING.md`
  - issue templates and PR template under `.github/`
- Release workflow `.github/workflows/release.yml` for Docker Hub and NuGet publishing on version tags.
- `samples/README.md` with end-to-end sample run instructions.
- Message API enhancements:
  - `POST /api/v1/messages/batch` for batch event enqueue.
  - `POST /api/v1/messages/replay` for replaying messages by date range and filters.
- Endpoint rate limiting support (`rateLimitPerMinute` in endpoint metadata) with worker integration.
- Dashboard event type management capabilities:
  - Backend CRUD endpoints under `/api/v1/dashboard/event-types`.
  - New dashboard page and navigation for event type management.
- Expanded filtering in dashboard message log (application, endpoint, date range).
- Additional automated coverage:
  - API integration tests for dashboard event type flow, dashboard message filters, and API key active/inactive behavior.
  - Repository tests for message and endpoint filtering/count/replay selection behavior.
  - Infrastructure/worker tests for endpoint health transitions, rate limiter behavior, and circuit breaker worker transitions.

### Changed
- `README.md` updated with documentation and samples links.
- `docs/ROADMAP.md` statuses updated for completed Phase 1 tasks (1.1-1.6).
- `docs/MVP-ROADMAP.md` updated to mark sample app completion.
- API response envelope standardization applied across dashboard/auth/application surfaces.
- Endpoint health now drives endpoint status transitions (`active`, `degraded`, `failed`) while preserving `disabled` endpoints.
- CI frontend workflow now includes explicit `yarn lint` step and uses `yarn typecheck`.

### Fixed
- Release workflow compatibility for tag releases by removing invalid job-level `if` conditions that referenced `secrets`.
