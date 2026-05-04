# ADR-002: CQRS Scaffold Removal

**Status:** Accepted
**Date:** 2026-03-30
**Context:** Phase 03 Structural Cleanup (REFR-02, GHIS-03)

## Context

`WebhookEngine.Application` was scaffolded with a MediatR-based CQRS folder structure including:

- `Applications/Commands/` and `Applications/Queries/`
- `Common/Behaviors/` and `Common/Mappings/`
- `Endpoints/Commands/` and `Endpoints/Queries/`
- `Messages/Commands/` and `Messages/Queries/`
- `DependencyInjection.cs` — a no-op service registration stub

The intent was to prepare for MediatR command/query handlers, but the controller-based flow remained the primary (and only) execution path. No MediatR handlers were ever implemented. The `.csproj` has no MediatR NuGet reference — confirming the CQRS pattern was never activated.

This leaves behind an entirely empty folder tree that misleads contributors: someone reading the project expects CQRS patterns to be in active use, may write new handlers expecting them to be wired, or may delay understanding the actual data flow by following dead directories.

## Decision

Remove all empty CQRS scaffold folders and `DependencyInjection.cs`. Retain the `WebhookEngine.Application` project in the solution (per D-05). The project continues to hold `Core` and `Infrastructure` project references and can host application-layer services, validators, or pipelines if needed in a future phase.

No MediatR packages are added at this time. If CQRS is adopted in a future milestone, folders will be recreated with actual implementations — not as empty placeholders.

## Consequences

### Positive

- Eliminates misleading folder structure; contributors see only code that is actually in use.
- Reduces cognitive overhead when navigating the Application project.
- Removes dead `DependencyInjection.cs` that could cause confusion about service registration.

### Negative

- If CQRS is adopted later, empty scaffold folders must be recreated (trivial cost — seconds of work).

### Neutral

- `WebhookEngine.Application` project stays in the solution; build and deployment are unchanged.
- The `.csproj` retains its `Core` and `Infrastructure` project references for future use.
- `AddApplicationServices` was never called from `Program.cs`, so no call site cleanup is needed.

## Update — 2026-05-05

The original decision retained the `WebhookEngine.Application` project in the solution as a placeholder for future application-layer code. After several months without any code being added to it, this rationale no longer holds — the empty project remained pure tech debt: an extra `.csproj`, an extra `ProjectReference` from the API host, an extra Dockerfile `COPY` line, and an empty `WebhookEngine.Application.Tests` project referencing it.

### Revised Decision

`WebhookEngine.Application` and `WebhookEngine.Application.Tests` are fully removed from the solution. Concretely:

- Solution entries for both projects deleted from `WebhookEngine.sln`
- `<ProjectReference>` to `WebhookEngine.Application` removed from `src/WebhookEngine.API/WebhookEngine.API.csproj`
- `src/WebhookEngine.Application/` and `tests/WebhookEngine.Application.Tests/` directories deleted
- `COPY src/WebhookEngine.Application/...` line removed from `docker/Dockerfile`
- Documentation references (`docs/ARCHITECTURE.md`, `README.md`, `CONTRIBUTING.md`, `AGENTS.md`, `CLAUDE.md`) updated

### Rationale for Revision

If a CQRS or application-services layer is needed in a future milestone (e.g. for the Phase 3 notification workflow engine or Phase 4 multi-tenant identity flows), the project will be **recreated from scratch** with concrete handlers from day one — not as an empty scaffold. The cost of recreating an empty `.csproj` is seconds; the cost of carrying a misleading empty project for months is contributor confusion and Dockerfile/sln drift.

### Consequences of Revision

- API project's dependency graph is now `Core ← Infrastructure ← Worker ← API` — strictly linear, no orphan branches.
- Solution file is shorter and cleaner; no GUIDs reference deleted projects.
- Future "application services" work starts with a clean recreation, with full intent documented from the first commit.
