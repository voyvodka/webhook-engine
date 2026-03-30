---
phase: 01-safety-foundations
plan: 03
subsystem: database
tags: [postgres, efcore, migration, check-constraint, hmac, signing-secret]

# Dependency graph
requires: []
provides:
  - EF Core migration with backfill + CHECK constraint on endpoints.secret_override
  - chk_endpoints_secret_override_not_empty constraint enforced at database level
affects: [02-performance-reliability, 03-structure-cleanup]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - Hand-written EF Core migration with raw SQL for CHECK constraints not expressible via fluent API
    - Two-step migration pattern: backfill then constrain

key-files:
  created:
    - src/WebhookEngine.Infrastructure/Migrations/20260330000001_EnforceEndpointSecretNotEmpty.cs
    - src/WebhookEngine.Infrastructure/Migrations/20260330000001_EnforceEndpointSecretNotEmpty.Designer.cs
  modified: []

key-decisions:
  - "Used raw SQL in migration (not EF fluent API) to keep model snapshot unchanged — SecretOverride stays nullable in EF config"
  - "Two-step Up(): backfill first, then add constraint — ensures no existing dirty data causes constraint violation on apply"
  - "CHECK constraint uses TRIM() to reject whitespace-only overrides, not just empty strings"

patterns-established:
  - "Pattern: Hand-written migration with Designer.cs copying model snapshot from previous migration when no model changes occur"
  - "Pattern: Backfill-before-constrain for safe schema migrations on existing data"

requirements-completed: [CORR-03]

# Metrics
duration: 5min
completed: 2026-03-30
---

# Phase 01 Plan 03: Enforce Endpoint SecretOverride Not-Empty Summary

**PostgreSQL CHECK constraint on endpoints.secret_override via EF Core migration with empty/whitespace backfill, enforcing signing secret integrity at the database level (CORR-03)**

## Performance

- **Duration:** ~5 min
- **Started:** 2026-03-30T10:00:00Z
- **Completed:** 2026-03-30T10:05:00Z
- **Tasks:** 1
- **Files modified:** 2

## Accomplishments

- Created hand-written EF Core migration (timestamp `20260330000001`) that backfills empty-string `secret_override` values to `NULL` before adding constraint
- Added `chk_endpoints_secret_override_not_empty` CHECK constraint: `secret_override IS NULL OR TRIM(secret_override) <> ''`
- Created companion Designer.cs file with full model snapshot so migration compiles and integrates with EF Core migration runner
- Infrastructure project builds with zero warnings and zero errors

## Task Commits

Each task was committed atomically:

1. **Task 1: Create EF Core migration for endpoint SecretOverride CHECK constraint** - `cb7af75` (feat)

**Plan metadata:** _(to be committed with SUMMARY.md)_

## Files Created/Modified

- `src/WebhookEngine.Infrastructure/Migrations/20260330000001_EnforceEndpointSecretNotEmpty.cs` - Migration with backfill UPDATE and CHECK constraint ALTER TABLE; Down() drops constraint
- `src/WebhookEngine.Infrastructure/Migrations/20260330000001_EnforceEndpointSecretNotEmpty.Designer.cs` - EF Core Designer partial class with full model snapshot (no model changes — snapshot copied from InitialCreate)

## Decisions Made

- Used raw SQL (`migrationBuilder.Sql`) instead of EF fluent API so the model snapshot is not affected — `SecretOverride` remains nullable in EF entity config by design
- Two-step `Up()`: backfill first then add constraint ensures migration applies cleanly to databases with existing empty-string records
- `TRIM()` in both backfill and constraint catches whitespace-only strings (e.g., `"   "`) not just empty strings

## Deviations from Plan

None — plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None — no external service configuration required.

Migration will be applied to the database when the user runs `dotnet ef database update` (not done by this plan per CLAUDE.md constraint).

## Next Phase Readiness

- CORR-03 signing secret constraint is in place — database enforces non-empty `secret_override` when provided
- `Application.SigningSecret` was already NOT NULL from `InitialCreate` — no additional migration needed for app-level secret
- Phase 02 performance work can proceed without dependency on this constraint

---
*Phase: 01-safety-foundations*
*Completed: 2026-03-30*
