# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
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
