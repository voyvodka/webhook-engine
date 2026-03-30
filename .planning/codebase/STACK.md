# Technology Stack

**Analysis Date:** 2026-03-30

## Languages

**Primary:**
- C# .NET 10.0 - Backend API, worker services, core business logic (`src/WebhookEngine.*`)
- TypeScript 5.9.3 - Dashboard frontend (`src/dashboard/src`)
- JavaScript/JSX - React components (`src/dashboard/src`)

**Secondary:**
- SQL - PostgreSQL queries in ORM and raw SQL for queue operations (`src/WebhookEngine.Infrastructure/Queue/PostgresMessageQueue.cs`)

## Runtime

**Environment:**
- .NET Runtime 10.0 - Primary backend runtime via `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`
- Node.js 20 (Alpine) - Dashboard build-only (not runtime)

**Package Manager:**
- NuGet - .NET packages
- Yarn - Frontend dependencies (specified in `src/dashboard/package.json`)
- Lockfile: `src/dashboard/yarn.lock` present

## Frameworks

**Core Backend:**
- ASP.NET Core 10.0 - Web API framework
- Entity Framework Core 10.0.3 - ORM for PostgreSQL data access (`src/WebhookEngine.Infrastructure`)
- Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0 - PostgreSQL provider for EF Core

**Frontend:**
- React 19.2.4 - UI component framework
- React Router 7.13.1 - Client-side routing
- Vite 7.3.1 - Frontend bundler and dev server
- Tailwind CSS 4.2.1 - Utility-first CSS framework
- TypeScript 5.9.3 - Type safety for JavaScript

**Business Logic:**
- MediatR 12.5.0 - CQRS pattern implementation (`src/WebhookEngine.Application/`)
- FluentValidation 12.1.1 - Input validation framework
- FluentValidation.AspNetCore 11.3.1 - ASP.NET Core integration
- FluentValidation.DependencyInjectionExtensions 12.1.1 - Dependency injection support

**Observability & Logging:**
- Serilog 4.3.1 - Structured logging
- Serilog.AspNetCore 10.0.0 - ASP.NET Core integration
- Serilog.Sinks.Console 6.1.1 - Console output sink
- OpenTelemetry 1.15.0 - Metrics and observability
- OpenTelemetry.Exporter.Prometheus.AspNetCore 1.15.0-beta.1 - Prometheus metrics export
- OpenTelemetry.Instrumentation.AspNetCore 1.15.0 - ASP.NET Core instrumentation
- OpenTelemetry.Instrumentation.Runtime 1.15.0 - Runtime metrics

**Real-time Communication:**
- Microsoft.AspNetCore.SignalR - WebSocket-based real-time updates for dashboard
- @microsoft/signalr 10.0.0 - SignalR client for dashboard

**HTTP & Network:**
- Microsoft.Extensions.Http 10.0.3 - Typed HTTP clients
- HttpClientFactory - Pooled HTTP client management for webhook delivery

**UI Components:**
- Recharts 3.7.0 - Chart library for dashboard visualizations
- Lucide-react 0.576.0 - Icon library

**Testing:**
- xUnit (implicit from .csproj test projects)

**Build/Dev:**
- Vite 7.3.1 - Frontend dev server with HMR
- ESLint 10.0.2 - JavaScript/TypeScript linting
- @tailwindcss/vite 4.2.1 - Tailwind CSS Vite plugin
- @vitejs/plugin-react 5.1.4 - React JSX/refresh plugin

## Key Dependencies

**Critical:**
- Entity Framework Core 10.0.3 - All database operations and schema management
- Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0 - PostgreSQL connectivity (production database)
- ASP.NET Core 10.0 - Entire backend runtime (API, worker services)
- React 19.2.4 - Dashboard UI foundation
- SignalR (@microsoft/signalr, Microsoft.AspNetCore.SignalR) - Real-time delivery status updates

**Infrastructure:**
- Serilog 4.3.1 + Serilog.AspNetCore 10.0.0 - Structured logging to stdout
- OpenTelemetry 1.15.0 + Prometheus exporter - Metrics collection and exposure
- MediatR 12.5.0 - Command/query handling pattern
- FluentValidation 12.1.1 - Input validation across layers

**Frontend Build:**
- Vite 7.3.1 - Builds to `src/WebhookEngine.API/wwwroot/` (embedded in .NET app)
- Tailwind CSS 4.2.1 - Styling

## Configuration

**Environment:**
- Configuration via `appsettings.json` in `src/WebhookEngine.API/appsettings.json`
- Environment-specific overrides: `appsettings.Development.json`, `appsettings.Production.json`
- Connection strings: `ConnectionStrings:Default` (PostgreSQL connection string)
- Configuration sections:
  - `WebhookEngine:Delivery` - Timeout, batch size, poll interval
  - `WebhookEngine:RetryPolicy` - Max retries, backoff schedule
  - `WebhookEngine:CircuitBreaker` - Failure threshold, cooldown, success threshold
  - `WebhookEngine:DashboardAuth` - Admin credentials
  - `WebhookEngine:Retention` - Data retention policies
  - `Serilog` - Logging configuration (JSON formatter to stdout)

**Build:**
- `src/dashboard/tsconfig.json` - TypeScript configuration
- `src/dashboard/vite.config.ts` (implicit) - Vite build configuration
- `src/dashboard/.eslintrc` (implicit) - ESLint configuration
- `docker/Dockerfile` - Multi-stage build (dashboard build → .NET publish → runtime)

## Platform Requirements

**Development:**
- .NET SDK 10.0 (for `dotnet` CLI)
- Node.js 20+ (for dashboard dependencies and build)
- Yarn (package manager, not npm)
- PostgreSQL 12+ (local development via `docker-compose.dev.yml`)

**Production:**
- .NET Runtime 10.0 (Alpine-based: `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`)
- PostgreSQL 17 (as specified in `docker-compose.yml`)
- Docker & Docker Compose (for containerized deployment)
- Memory: Min 128MB app + 64MB database per compose file resource limits

---

*Stack analysis: 2026-03-30*
