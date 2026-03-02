# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- No unreleased changes yet.

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
