<!-- GSD:project-start source:PROJECT.md -->
## Project

**WebhookEngine**

Queue-based webhook delivery engine with retry logic, circuit breaker, HMAC signing, and a React dashboard for monitoring. .NET 10 backend, PostgreSQL for persistence and queue, SignalR for real-time updates. Self-hosted via Docker Compose.

**Core Value:** Reliable, observable webhook delivery — messages must reach their endpoints with guaranteed retry, proper signing, and full delivery visibility.

### Constraints

- **Tech stack**: .NET 10, React 19, PostgreSQL — no stack changes
- **Breaking changes**: Avoid API breaking changes; this is a patch-level stabilization
- **Release**: Main branch stabilization; release cut planned separately
- **Package manager**: Yarn for frontend, NuGet for backend
<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->
## Technology Stack

## Languages
- C# .NET 10.0 - Backend API, worker services, core business logic (`src/WebhookEngine.*`)
- TypeScript 5.9.3 - Dashboard frontend (`src/dashboard/src`)
- JavaScript/JSX - React components (`src/dashboard/src`)
- SQL - PostgreSQL queries in ORM and raw SQL for queue operations (`src/WebhookEngine.Infrastructure/Queue/PostgresMessageQueue.cs`)
## Runtime
- .NET Runtime 10.0 - Primary backend runtime via `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`
- Node.js 20 (Alpine) - Dashboard build-only (not runtime)
- NuGet - .NET packages
- Yarn - Frontend dependencies (specified in `src/dashboard/package.json`)
- Lockfile: `src/dashboard/yarn.lock` present
## Frameworks
- ASP.NET Core 10.0 - Web API framework
- Entity Framework Core 10.0.3 - ORM for PostgreSQL data access (`src/WebhookEngine.Infrastructure`)
- Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0 - PostgreSQL provider for EF Core
- React 19.2.4 - UI component framework
- React Router 7.13.1 - Client-side routing
- Vite 7.3.1 - Frontend bundler and dev server
- Tailwind CSS 4.2.1 - Utility-first CSS framework
- TypeScript 5.9.3 - Type safety for JavaScript
- MediatR 12.5.0 - CQRS pattern implementation (`src/WebhookEngine.Application/`)
- FluentValidation 12.1.1 - Input validation framework
- FluentValidation.AspNetCore 11.3.1 - ASP.NET Core integration
- FluentValidation.DependencyInjectionExtensions 12.1.1 - Dependency injection support
- Serilog 4.3.1 - Structured logging
- Serilog.AspNetCore 10.0.0 - ASP.NET Core integration
- Serilog.Sinks.Console 6.1.1 - Console output sink
- OpenTelemetry 1.15.0 - Metrics and observability
- OpenTelemetry.Exporter.Prometheus.AspNetCore 1.15.0-beta.1 - Prometheus metrics export
- OpenTelemetry.Instrumentation.AspNetCore 1.15.0 - ASP.NET Core instrumentation
- OpenTelemetry.Instrumentation.Runtime 1.15.0 - Runtime metrics
- Microsoft.AspNetCore.SignalR - WebSocket-based real-time updates for dashboard
- @microsoft/signalr 10.0.0 - SignalR client for dashboard
- Microsoft.Extensions.Http 10.0.3 - Typed HTTP clients
- HttpClientFactory - Pooled HTTP client management for webhook delivery
- Recharts 3.7.0 - Chart library for dashboard visualizations
- Lucide-react 0.576.0 - Icon library
- xUnit (implicit from .csproj test projects)
- Vite 7.3.1 - Frontend dev server with HMR
- ESLint 10.0.2 - JavaScript/TypeScript linting
- @tailwindcss/vite 4.2.1 - Tailwind CSS Vite plugin
- @vitejs/plugin-react 5.1.4 - React JSX/refresh plugin
## Key Dependencies
- Entity Framework Core 10.0.3 - All database operations and schema management
- Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0 - PostgreSQL connectivity (production database)
- ASP.NET Core 10.0 - Entire backend runtime (API, worker services)
- React 19.2.4 - Dashboard UI foundation
- SignalR (@microsoft/signalr, Microsoft.AspNetCore.SignalR) - Real-time delivery status updates
- Serilog 4.3.1 + Serilog.AspNetCore 10.0.0 - Structured logging to stdout
- OpenTelemetry 1.15.0 + Prometheus exporter - Metrics collection and exposure
- MediatR 12.5.0 - Command/query handling pattern
- FluentValidation 12.1.1 - Input validation across layers
- Vite 7.3.1 - Builds to `src/WebhookEngine.API/wwwroot/` (embedded in .NET app)
- Tailwind CSS 4.2.1 - Styling
## Configuration
- Configuration via `appsettings.json` in `src/WebhookEngine.API/appsettings.json`
- Environment-specific overrides: `appsettings.Development.json`, `appsettings.Production.json`
- Connection strings: `ConnectionStrings:Default` (PostgreSQL connection string)
- Configuration sections:
- `src/dashboard/tsconfig.json` - TypeScript configuration
- `src/dashboard/vite.config.ts` (implicit) - Vite build configuration
- `src/dashboard/.eslintrc` (implicit) - ESLint configuration
- `docker/Dockerfile` - Multi-stage build (dashboard build → .NET publish → runtime)
## Platform Requirements
- .NET SDK 10.0 (for `dotnet` CLI)
- Node.js 20+ (for dashboard dependencies and build)
- Yarn (package manager, not npm)
- PostgreSQL 12+ (local development via `docker-compose.dev.yml`)
- .NET Runtime 10.0 (Alpine-based: `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`)
- PostgreSQL 17 (as specified in `docker-compose.yml`)
- Docker & Docker Compose (for containerized deployment)
- Memory: Min 128MB app + 64MB database per compose file resource limits
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->
## Conventions

## C# Backend Conventions
### Naming Patterns
- PascalCase for all C# files: `ApplicationRepository.cs`, `DeliveryWorker.cs`, `ExceptionHandlingMiddleware.cs`
- One public class per file (strict adherence)
- Filename matches class name exactly
- PascalCase for class names: `Application`, `ApplicationRepository`, `HttpDeliveryService`
- PascalCase for method names: `GetByIdAsync`, `CreateAsync`, `ProcessMessageAsync`
- PascalCase for properties: `Id`, `Name`, `ApiKeyPrefix`, `IsActive`, `CreatedAt`
- Async methods always end with `Async` suffix: `DeliverAsync`, `GetByIdAsync`, `ProcessMessageAsync`
- camelCase for local variables and parameters: `ct` (CancellationToken), `message`, `messageId`
- camelCase for private fields with underscore prefix: `_dbContext`, `_logger`, `_httpClientFactory`, `_serviceProvider`
- camelCase for parameters in method signatures: `applicationId`, `request`, `pageSize`
- PascalCase for enum types: `EndpointStatus`, `MessageStatus`, `AttemptStatus`, `CircuitState`
- PascalCase for enum values: `Active`, `Pending`, `Delivered`, `Failed`, `DeadLetter`, `Disabled`
- PascalCase for interface names with `I` prefix: `IDeliveryService`, `IMessageQueue`, `ISigningService`, `IEndpointHealthTracker`
- PascalCase for static readonly constants and magic strings in usage context
### Import Organization
### Code Style
- No explicit tool (Roslyn analyzers only)
- 4 spaces for indentation
- PascalCase for namespaces matching folder structure: `WebhookEngine.API.Controllers`, `WebhookEngine.Infrastructure.Repositories`
- Nullable reference types enabled: `<Nullable>enable</Nullable>`
- No trailing commas in collections
- Roslyn code analyzers implicit via `<Nullable>enable</Nullable>` and null-safety checking
- No explicit ESLint or Prettier config in C# projects
- Code follows Microsoft C# coding conventions
### Comments and Documentation
- Method and class-level summaries using `///` XML documentation
- Critical business logic explained inline with `//`
- URL references and security notes: `// NOTE:`, `// IMPORTANT:`, `// TODO:`
- Explain "why" not "what": "Generate API key: whe_{appIdShort}_{random32}" for clarity
- Short, direct explanations
- No block comment style
### Function/Method Design
- Methods typically 20-100 lines for business logic
- Background workers (DeliveryWorker) can be longer (100+ lines) due to loop structure and multiple state transitions
- Private helper methods extracted for clarity
- CancellationToken always last parameter: `async Task<T> MethodAsync(param1, param2, CancellationToken ct = default)`
- `ct` is the standard short name for CancellationToken
- Requests use `[FromBody]` or `[FromQuery]` attributes in controllers
- IOptions<T> for configuration injections
- Async methods always return `Task` or `Task<T>`, never bare async operations
- `await` is used consistently; no fire-and-forget except explicit `void connection.Start().catch(...)`
- Early returns for validation and error checks: `if (application is null) return NotFound(...)`
### Module Design
- Controllers are public and inherit from `ControllerBase`
- Request/response DTOs are public inline in controller file or separate file
- Repositories are scoped services, registered in DI container
- Services implement interfaces for dependency injection
- Not used; explicit imports preferred
### Error Handling
- Null checks: `if (application is null) return NotFound(...)`
- Try-catch in background workers to prevent crashes: `catch (Exception ex) { _logger.LogError(...); }`
- Custom exception handling middleware: `ExceptionHandlingMiddleware` wraps all exceptions
- API responses use consistent envelope: `ApiEnvelope.Error(HttpContext, code, message)`
- Validation using FluentValidation validators: `AbstractValidator<T>` implementations
### Logging
- Configuration: `builder.Host.UseSerilog((context, configuration) => configuration.ReadFrom.Configuration(context.Configuration))`
- ILogger<T> dependency injection
- Named loggers per class: `ILogger<DeliveryWorker>`, `ILogger<ExceptionHandlingMiddleware>`
- Info level for worker start/stop: `_logger.LogInformation("DeliveryWorker started. WorkerId: {WorkerId}", _workerId)`
- Error level with exception: `_logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path)`
- Warning level for expected failures: `console.warn("SignalR connection failed:", err)`
- Structured logging with named placeholders: `{WorkerId}`, `{Method}`, `{Path}`
## TypeScript/React Dashboard Conventions
### Naming Patterns
- PascalCase for React components: `PayloadViewer.tsx`, `Modal.tsx`, `EventTypeSelect.tsx`
- camelCase for utilities and hooks: `authApi.ts`, `useDeliveryFeed.ts`, `dateTime.ts`
- camelCase for types files: `types.ts`, `vite-env.d.ts`
- PascalCase for React components: `PayloadViewer`, `Modal`, `ConfirmModal`
- camelCase for utility functions: `login`, `logout`, `getCurrentUser`, `parseError`
- camelCase for custom hooks: `useDeliveryFeed`, `useAuthContext`
- Const arrow functions standard: `export const PayloadViewer = ({ value }: PayloadViewerProps) => { ... }`
- camelCase for all variables and parameters: `maxEvents`, `events`, `connected`, `isMounted`
- camelCase for ref names: `connectionRef`, `containerRef`
- camelCase for event handlers: `handleSuccess`, `handleFailure`, `handleClose`
- PascalCase for interface/type names: `DeliveryEvent`, `AuthUser`, `EndpointRow`, `MessageRow`
- `Props` suffix for component prop types: `PayloadViewerProps`, `EventTypeSelectProps`
- Leading underscore for unused parameters: `...WithMessage("at least one field must be provided.")` where message is unused
### Import Organization
### Code Style
- ESLint with TypeScript support: `eslint.config.js`
- TypeScript target: ES2020, module: ESNext
- Strict mode enabled: `"strict": true` in tsconfig.json
- Isolated modules: `"isolatedModules": true`
- `@typescript-eslint/no-unused-vars`: warn for unused variables (except those starting with `_`)
- `@typescript-eslint/no-explicit-any`: warn for `any` type usage
- `no-console`: warn except for `warn` and `error` methods
- `prefer-const`: error — always use const
- React hooks: `eslint-plugin-react-hooks` recommended rules
### Comments and Documentation
- JSDoc comments for exported functions and types: `/** Connects to the SignalR /hubs/deliveries endpoint ... */`
- Inline comments only for non-obvious logic
- URL protocol explanations: `credentials: "include"` comment explaining CORS cookie behavior
### Function/Component Design
- React components typically 20-50 lines for simple presentational components
- Custom hooks (useDeliveryFeed) can be 100+ lines for complex state management and side effects
- Arrow functions preferred: `const handleSuccess = (data: DeliveryEvent) => { ... }`
- Props destructured in function signature: `({ value }: PayloadViewerProps)`
- Callback dependencies tracked in useEffect: `useEffect(() => { ... }, [push])`
- Optional parameters in interfaces: `lastLoginAt?: string | null`
- Components return JSX or null
- Custom hooks return objects with typed properties: `return { events, connected }`
- API functions return Promise-wrapped types: `Promise<AuthUser>`, `Promise<AuthUser | null>`
- Callback functions use void: `const push = useCallback((event: DeliveryEvent) => { ... }, [maxEvents])`
### Module Design
- Named exports for everything: `export function useDeliveryFeed(...)`, `export interface DeliveryEvent`
- One component per file
- API module exports functions: `export async function login(...)`, `export async function logout(...)`
- Types module exports all type definitions
- Not explicitly used; direct imports from source files preferred
### Error Handling
- Try-catch with fallback in parseError: `try { const payload = ... } catch { return \`Request failed...\` }`
- Null checks: `if (!response.ok) throw new Error(...)`
- Expected error types handled: `if (response.status === 401) return null`
- Mounted flag pattern for cleanup in effects: `if (!isMounted) return;`
### Logging
- Warning level for non-critical issues: `console.warn("SignalR connection failed:", err)`
- Only warn and error allowed by ESLint config
- No info/debug logging in normal flow
### Type Safety
- Strict typing everywhere: `interface AuthUser { id: string; email: string; role: string; ... }`
- Union types for status: `type MessageStatusType = "Pending" | "Sending" | "Delivered" | "Failed" | "DeadLetter"`
- Envelopes for API responses: `interface Envelope<T> { data: T }`
- Type assertion in API parsing: `const payload = (await response.json()) as Envelope<AuthUser>`
- Optional properties in types: `eventTypeIds?: string[]`, `error?: string`
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->
## Architecture

## Pattern Overview
- Clear separation between Core, Application, Infrastructure, API, and Worker layers
- Queue-based message processing with optimistic locking for distributed delivery
- Interface-driven design for service abstraction (IMessageQueue, IDeliveryService, ISigningService)
- Real-time delivery notifications via SignalR push to dashboard
- Entity Framework Core with PostgreSQL for persistence
- Background workers as hosted services for asynchronous processing
## Layers
- Purpose: Domain models, business logic abstractions, and options
- Location: `src/WebhookEngine.Core/`
- Contains: Entities, Enums, Interfaces, Models, Metrics, Options
- Depends on: Nothing (no external dependencies)
- Used by: Application, Infrastructure, API, Worker layers
- Purpose: Concrete implementations of interfaces, database operations, external service integration
- Location: `src/WebhookEngine.Infrastructure/`
- Contains: Database context (EF Core), repositories, queue implementation, delivery service, signing service, health tracking
- Depends on: Core
- Used by: API, Worker layers
- Purpose: Application orchestration and use-case coordination
- Location: `src/WebhookEngine.Application/`
- Contains: MediatR registration (currently minimal, scaffolded structure with Commands/Queries folders)
- Depends on: Core, Infrastructure
- Used by: API layer
- Purpose: HTTP endpoints, middleware, controllers, authentication, real-time hubs
- Location: `src/WebhookEngine.API/`
- Contains: Controllers, middleware, SignalR hubs, validators, auth, startup code
- Depends on: Core, Infrastructure, Application, Worker
- Used by: HTTP clients
- Special: Hosts the React dashboard from `wwwroot/`
- Purpose: Background jobs and asynchronous processing
- Location: `src/WebhookEngine.Worker/`
- Contains: Background services (DeliveryWorker, RetryScheduler, CircuitBreakerWorker, RetentionCleanupWorker, StaleLockRecoveryWorker)
- Depends on: Core, Infrastructure
- Used by: API (registered as hosted services in DI)
- Purpose: .NET client library for consuming WebhookEngine API
- Location: `src/WebhookEngine.Sdk/`
- Contains: HTTP client wrappers, models, helpers
- Depends on: Nothing
- Used by: External applications
## Data Flow
- **Message Status:** Pending → Sending → Delivered/Failed/DeadLettered
- **Endpoint Health Circuit State:** Closed → Open → HalfOpen → Closed
- **Distributed Locking:** LockedAt + LockedBy on Message entity prevents duplicate processing
- **Optimistic Concurrency:** Worker IDs included in lock to aid debugging; staleness detected by timestamp
## Key Abstractions
- Purpose: Abstraction for message persistence and queue polling
- Examples: `src/WebhookEngine.Infrastructure/Queue/PostgresMessageQueue.cs`
- Pattern: Uses raw SQL with `FOR UPDATE SKIP LOCKED` for atomic dequeue operations without full locking
- Purpose: Abstraction for webhook delivery (HTTP POST to endpoints)
- Examples: `src/WebhookEngine.Infrastructure/Services/HttpDeliveryService.cs`
- Pattern: Returns DeliveryResult with status code, latency, response body; truncates responses to 10KB max
- Purpose: HMAC signature generation for webhook authentication
- Examples: `src/WebhookEngine.Infrastructure/Services/HmacSigningService.cs`
- Pattern: Implements Svix-compatible webhook signing with webhook-id, webhook-timestamp, webhook-signature headers
- Purpose: Tracks endpoint health metrics and failure rates
- Examples: `src/WebhookEngine.Infrastructure/Services/EndpointHealthTracker.cs`
- Pattern: Singleton service tracking consecutive failures and last success/failure timestamps
- Purpose: Rate limiting per endpoint to prevent overwhelming subscribers
- Examples: `src/WebhookEngine.Infrastructure/Services/EndpointRateLimiter.cs`
- Pattern: Singleton service; may use token bucket or sliding window algorithm
- Purpose: Real-time push notifications for delivery status updates
- Examples: `src/WebhookEngine.API/Hubs/DeliveryHub.cs` (SignalRDeliveryNotifier implementation)
- Pattern: SignalR-based; singleton scoped to allow workers to inject and notify dashboard
## Entry Points
- Location: `src/WebhookEngine.API/Program.cs`
- Triggers: ASP.NET Core web host startup
- Responsibilities: Configure DI, database migrations, middleware pipeline, OpenTelemetry, background workers, static file serving for dashboard
- Location: `src/WebhookEngine.Worker/DeliveryWorker.cs`
- Triggers: Hosted service on startup; runs continuously in background
- Responsibilities: Poll queue, dequeue batches of pending messages, coordinate delivery attempts, update message status, notify subscribers
- Location: `src/WebhookEngine.Worker/RetryScheduler.cs`
- Triggers: Hosted service on startup; runs on timer interval
- Responsibilities: Find messages past their scheduledAt time, re-enqueue for delivery
- Location: `src/WebhookEngine.Worker/CircuitBreakerWorker.cs`
- Triggers: Hosted service on startup; runs periodically
- Responsibilities: Monitor endpoint health, trip/reset circuit breaker, enforce cooldown
- Location: `src/WebhookEngine.Worker/RetentionCleanupWorker.cs`
- Triggers: Hosted service on startup; runs on schedule
- Responsibilities: Hard-delete old messages and attempts beyond retention period
- Location: `src/WebhookEngine.Worker/StaleLockRecoveryWorker.cs`
- Triggers: Hosted service on startup; runs periodically
- Responsibilities: Detect and release locks held > configured duration to prevent message loss
## Error Handling
- **Validation Errors:** FluentValidation validators run automatically via ASP.NET Core integration; return 400 Bad Request with details
- **Business Logic Errors:** Controllers return `UnprocessableEntity` (422) with error code and message in ApiEnvelope
- **Unhandled Exceptions:** ExceptionHandlingMiddleware catches and logs; returns 500 Internal Server Error
- **Queue Delivery Failures:** Caught in DeliveryWorker; logged but don't crash worker; message re-queued with retry logic
- **HTTP Timeouts:** HttpClient timeout set to 30 seconds (configurable); delivery marked failed and retried
- **Database Failures:** EF Core exceptions propagate; can trigger health check failure in containers
## Cross-Cutting Concerns
<!-- GSD:architecture-end -->

## Release & Versioning

### Versioning
- Semantic versioning: `v{major}.{minor}.{patch}` (e.g., v0.1.0, v0.1.1, v1.0.0)
- Tags: always prefixed with `v` (e.g., `v0.1.1`)
- Annotated tags with summary of changes

### Release Workflow
When creating a release (tag + push + GitHub release):

1. **Pre-release checks**: Run full local CI simulation before tagging:
   - `dotnet build WebhookEngine.sln --configuration Release` (0 errors, 0 warnings)
   - `dotnet test WebhookEngine.sln --no-build --configuration Release` (all tests pass)
   - `cd src/dashboard && yarn lint && yarn typecheck && yarn build` (all pass)

2. **Commit & Tag**: Commit pending changes, create annotated tag, push both

3. **GitHub Release**: Create via `gh release create` with:
   - Title: `v{version} — {Short Name}` (e.g., `v0.1.1 — Stabilization Patch`)
   - Body structure:
     ```
     ## WebhookEngine v{version}

     {1-2 sentence summary}

     ### Features / Fixes / Changes
     - **category:** description

     ### Quick Start (for major/minor releases)
     docker pull + compose command

     ### Links
     - Docker Hub, NuGet, docs links
     ```

4. **Verify**: Confirm CI and Release workflows pass on GitHub Actions

### CI/CD Pipelines
- **CI** (`ci.yml`): Triggers on push to main — backend build+test, frontend lint+typecheck+build, Docker build
- **Release** (`release.yml`): Triggers on `v*` tags — publishes Docker image to Docker Hub (`voyvodka/webhook-engine`) and NuGet package (`WebhookEngine.Sdk`)

### GitHub Repository Settings
Keep these in sync when releasing:
- **Description**: Matches project summary
- **Homepage**: Docker Hub link
- **Topics**: Keep relevant (webhook, dotnet, react, docker, etc.)
- **Releases**: Every tag gets a GitHub release with detailed notes

<!-- GSD:workflow-start source:GSD defaults -->
## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd:quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd:debug` for investigation and bug fixing
- `/gsd:execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->



<!-- GSD:profile-start -->
## Developer Profile

> Profile not yet configured. Run `/gsd:profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
