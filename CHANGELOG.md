# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

### Changed
- `README.md` updated with documentation and samples links.
- `docs/ROADMAP.md` statuses updated for completed Phase 1 tasks (1.1-1.6).
- `docs/MVP-ROADMAP.md` updated to mark sample app completion.
