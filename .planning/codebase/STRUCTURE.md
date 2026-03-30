# Codebase Structure

**Analysis Date:** 2026-03-30

## Directory Layout

```
webhook-engine/
├── src/
│   ├── WebhookEngine.Core/              # Domain entities, interfaces, enums, options
│   ├── WebhookEngine.Infrastructure/    # Database, repositories, queue, services
│   ├── WebhookEngine.Application/       # MediatR handlers, use cases (minimal)
│   ├── WebhookEngine.API/               # ASP.NET Core web host, controllers, middleware
│   ├── WebhookEngine.Worker/            # Background services (delivery, retry, cleanup)
│   ├── WebhookEngine.Sdk/               # .NET client library for WebhookEngine API
│   └── dashboard/                       # React frontend (TypeScript)
├── tests/
│   ├── WebhookEngine.Core.Tests/
│   ├── WebhookEngine.Infrastructure.Tests/
│   ├── WebhookEngine.Application.Tests/
│   ├── WebhookEngine.API.Tests/
│   └── WebhookEngine.Worker.Tests/
├── samples/                              # Example projects using the SDK
├── docker/                               # Docker build files
├── docs/                                 # Documentation
├── scripts/                              # Build and utility scripts
└── WebhookEngine.sln                    # Solution file
```

## Directory Purposes

**src/WebhookEngine.Core/:**
- Purpose: Domain-driven design core; zero dependencies on external libraries or other projects
- Contains: Entity definitions, enumerations, interfaces for abstraction, configuration options, metrics definitions
- Key files: `Entities/`, `Enums/`, `Interfaces/`, `Models/`, `Options/`, `Metrics/`

**src/WebhookEngine.Infrastructure/:**
- Purpose: Persistence, external service integrations, concrete implementations of Core interfaces
- Contains: EF Core DbContext, repositories (data access), queue implementation, HTTP delivery service, signing service, health tracking
- Key files: `Data/WebhookDbContext.cs`, `Repositories/`, `Queue/PostgresMessageQueue.cs`, `Services/`

**src/WebhookEngine.Application/:**
- Purpose: Application orchestration layer; currently minimally used (MediatR registration only)
- Contains: Scaffolded command/query structure for future expansion; dependency injection setup
- Key files: `DependencyInjection.cs`, `Applications/Commands/`, `Endpoints/Queries/`, `Messages/Commands/`
- Note: Most business logic lives in API controllers and worker services currently

**src/WebhookEngine.API/:**
- Purpose: HTTP API and web host; entry point for the application
- Contains: ASP.NET Core startup, controllers, middleware, authentication, SignalR hubs, validators
- Key subdirectories:
  - `Controllers/`: REST endpoints (Applications, Endpoints, EventTypes, Messages, Health, Auth, Dashboard)
  - `Middleware/`: Request logging, exception handling, API key authentication
  - `Hubs/`: SignalR for real-time delivery notifications
  - `Auth/`: Password hashing for dashboard users
  - `Contracts/`: DTOs and request/response envelopes
  - `Validators/`: FluentValidation request validators
  - `Startup/`: Database seeding for initial dashboard admin
  - `wwwroot/`: Static files (compiled React dashboard)
- Key files: `Program.cs` (startup), `Validators/RequestValidators.cs`

**src/WebhookEngine.Worker/:**
- Purpose: Background job processing; runs as hosted services alongside API
- Contains: Worker implementations that poll queue, retry failed messages, monitor health, cleanup old data
- Key files:
  - `DeliveryWorker.cs`: Main message delivery polling loop
  - `RetryScheduler.cs`: Re-schedules pending messages
  - `CircuitBreakerWorker.cs`: Monitors endpoint health and trip/reset circuit breaker
  - `RetentionCleanupWorker.cs`: Hard-deletes old messages beyond retention
  - `StaleLockRecoveryWorker.cs`: Releases stale distributed locks

**src/WebhookEngine.Sdk/:**
- Purpose: .NET client library for external applications to call WebhookEngine API
- Contains: HTTP client wrappers, DTOs, helper utilities
- Key files: `WebhookEngineClient.cs` (main client), `MessageClient.cs`, `EndpointClient.cs`, `EventTypeClient.cs`, `Models.cs`

**src/dashboard/:**
- Purpose: React TypeScript frontend for management and monitoring
- Contains: UI components, API integration, real-time updates via SignalR
- Key subdirectories: `src/api/`, `src/components/`, `src/pages/`, `src/hooks/`, `src/auth/`, `src/routes/`
- Build output: Compiled into `src/WebhookEngine.API/wwwroot/` for static serving

**tests/:**
- Purpose: Unit and integration tests for each layer
- Structure: Mirrored to source projects (e.g., `WebhookEngine.API.Tests/` tests `src/WebhookEngine.API/`)
- Test frameworks: xUnit, Moq, Testcontainers (for PostgreSQL integration tests)

## Key File Locations

**Entry Points:**
- `src/WebhookEngine.API/Program.cs`: ASP.NET Core startup, DI configuration, middleware pipeline, worker registration
- `src/WebhookEngine.API/Startup/DashboardAdminSeeder.cs`: Initializes default dashboard admin user from environment or config

**Configuration:**
- `appsettings.json` (in WebhookEngine.API): Connection strings, logging, feature toggles, worker intervals
- `WebhookEngine.sln`: Solution file defining project dependencies

**Core Domain:**
- `src/WebhookEngine.Core/Entities/`: Application, Endpoint, Message, EventType, MessageAttempt, EndpointHealth, DashboardUser
- `src/WebhookEngine.Core/Enums/`: MessageStatus, EndpointStatus, CircuitState, AttemptStatus
- `src/WebhookEngine.Core/Interfaces/`: IMessageQueue, IDeliveryService, ISigningService, IEndpointHealthTracker, IEndpointRateLimiter, IDeliveryNotifier

**Database:**
- `src/WebhookEngine.Infrastructure/Data/WebhookDbContext.cs`: EF Core DbContext; model configurations for all entities
- `src/WebhookEngine.Infrastructure/Migrations/`: EF Core migrations (currently one initial migration: 20260227060707_InitialCreate)

**API Controllers:**
- `src/WebhookEngine.API/Controllers/MessagesController.cs`: Send messages, list messages
- `src/WebhookEngine.API/Controllers/EndpointsController.cs`: CRUD operations on endpoints
- `src/WebhookEngine.API/Controllers/ApplicationsController.cs`: Application management
- `src/WebhookEngine.API/Controllers/EventTypesController.cs`: Event type definitions
- `src/WebhookEngine.API/Controllers/AuthController.cs`: Dashboard login/logout
- `src/WebhookEngine.API/Controllers/DashboardController.cs`: Dashboard statistics and queries
- `src/WebhookEngine.API/Controllers/HealthController.cs`: Health check endpoint

**Testing:**
- `tests/WebhookEngine.API.Tests/`: Integration tests using WebApplicationFactory
- `tests/WebhookEngine.Infrastructure.Tests/`: Database and repository tests
- `tests/WebhookEngine.Worker.Tests/`: Background service tests

## Naming Conventions

**Files:**
- `[EntityName].cs`: Single entity or interface (e.g., `Message.cs`, `IMessageQueue.cs`)
- `[FeatureName]Controller.cs`: API controllers (e.g., `MessagesController.cs`)
- `[FeatureName]Repository.cs`: Data access classes (e.g., `MessageRepository.cs`)
- `[FeatureName]Service.cs`: Service implementations (e.g., `HttpDeliveryService.cs`)
- `[FeatureName]Worker.cs`: Background service workers (e.g., `DeliveryWorker.cs`)
- `[FeatureName]Tests.cs`: Test classes (e.g., `DeliveryWorkerTests.cs`)

**Classes:**
- `I[Name]`: Interfaces for abstractions
- `[Name]Entity` or just `[Name]`: EF Core entities
- `[Name]Repository`: Data access classes
- `[Name]Service`: Service implementations
- `[Name]Worker`: Hosted background services
- `[Name]Client`: SDK clients
- `[Name]Options`: Configuration option classes

**Directories:**
- `Entities/`: Domain entity classes
- `Enums/`: Enumeration types
- `Interfaces/`: Interface definitions
- `Models/`: Data transfer or value objects
- `Options/`: Configuration classes
- `Services/`: Service implementations
- `Repositories/`: Data access
- `Controllers/`: ASP.NET Core controllers
- `Middleware/`: HTTP middleware
- `Hubs/`: SignalR hubs
- `Validators/`: FluentValidation rule sets
- `Commands/`, `Queries/`: MediatR command/query handlers (in Application layer)

## Where to Add New Code

**New Feature (e.g., webhook template system):**
- Domain entity: `src/WebhookEngine.Core/Entities/WebhookTemplate.cs`
- Options/config: `src/WebhookEngine.Core/Options/WebhookTemplateOptions.cs` if needed
- Interface: `src/WebhookEngine.Core/Interfaces/IWebhookTemplateService.cs`
- Implementation: `src/WebhookEngine.Infrastructure/Services/WebhookTemplateService.cs`
- Repository: `src/WebhookEngine.Infrastructure/Repositories/WebhookTemplateRepository.cs`
- Controller: `src/WebhookEngine.API/Controllers/WebhookTemplatesController.cs`
- Validator: `src/WebhookEngine.API/Validators/RequestValidators.cs` (add validator for requests)
- Tests: `tests/WebhookEngine.API.Tests/Controllers/WebhookTemplatesControllerTests.cs`

**New Background Worker (e.g., audit log archiver):**
- Implementation: `src/WebhookEngine.Worker/AuditLogArchiverWorker.cs` inheriting BackgroundService
- Register in DI: Add to `src/WebhookEngine.API/Program.cs` as `builder.Services.AddHostedService<AuditLogArchiverWorker>();`
- Tests: `tests/WebhookEngine.Worker.Tests/AuditLogArchiverWorkerTests.cs`

**New Service/Repository:**
- Interface in Core: `src/WebhookEngine.Core/Interfaces/I[Feature]Service.cs`
- Implementation in Infrastructure: `src/WebhookEngine.Infrastructure/Services/[Feature]Service.cs` or `Repositories/[Feature]Repository.cs`
- Register in DI: If not auto-discovered, add to Program.cs or DependencyInjection extension method

**New Endpoint/Controller Action:**
- Add to appropriate controller in `src/WebhookEngine.API/Controllers/` (or create new controller if new domain)
- Add FluentValidation rules in `src/WebhookEngine.API/Validators/RequestValidators.cs` if input validation needed
- Return ApiEnvelope-wrapped response using `ApiEnvelope.Success()` or `ApiEnvelope.Error()`
- Add tests in corresponding test controller file

**New SDK Client:**
- Add client class in `src/WebhookEngine.Sdk/` (e.g., `AuditLogClient.cs`)
- Expose in `WebhookEngineClient` class via public property
- Add models to `src/WebhookEngine.Sdk/Models.cs`

**Dashboard Frontend:**
- Components: `src/dashboard/src/components/[Feature]/`
- Pages: `src/dashboard/src/pages/[Feature].tsx`
- API calls: `src/dashboard/src/api/[feature]Api.ts` (or extend existing)
- Routes: Register in `src/dashboard/src/routes/`

## Special Directories

**src/WebhookEngine.API/wwwroot/:**
- Purpose: Static files served by ASP.NET Core
- Contents: Compiled React dashboard (built from `src/dashboard/`)
- Generated: Yes (compiled by dashboard build process)
- Committed: No (generated artifacts; .gitignore excludes or includes per build strategy)

**src/WebhookEngine.Infrastructure/Migrations/:**
- Purpose: EF Core migration files for schema changes
- Generated: By `dotnet ef migrations add` command
- Committed: Yes (part of version control; tracks database schema evolution)

**docker/:**
- Purpose: Dockerfile and docker-compose for containerization
- Contents: Multi-stage builds, PostgreSQL service definition
- Committed: Yes

**docs/:**
- Purpose: Project documentation, API specifications, deployment guides
- Contents: Architecture, configuration, examples, deployment guides
- Committed: Yes

**artifacts/**
- Purpose: Build output directory (binaries, packages)
- Generated: Yes
- Committed: No (.gitignore)

---

*Structure analysis: 2026-03-30*
