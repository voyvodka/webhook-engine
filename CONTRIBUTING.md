# Contributing to WebhookEngine

Thanks for your interest in contributing! This guide covers how to set up the project and submit changes.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/) and [Yarn](https://yarnpkg.com/)
- [Docker](https://www.docker.com/) (for PostgreSQL)

## Local Development Setup

### 1. Start PostgreSQL

```bash
docker compose -f docker/docker-compose.dev.yml up -d
```

This starts PostgreSQL 17 on `localhost:5432` (user: `postgres`, password: `postgres`, database: `webhookengine`).

### 2. Build & Run the API

```bash
dotnet build WebhookEngine.sln
dotnet run --project src/WebhookEngine.API
```

The API starts at `http://localhost:5128`. Database migrations are applied automatically on startup.

### 3. Build & Run the Dashboard

```bash
cd src/dashboard
yarn install
yarn dev
```

The dev server starts at `http://localhost:5173` and proxies API requests to `localhost:5128`.

For production builds:

```bash
yarn build
```

This outputs to the API's `wwwroot/` directory.

## Running Tests

```bash
# All tests
dotnet test WebhookEngine.sln

# Specific project
dotnet test tests/WebhookEngine.Core.Tests

# Specific test
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"
```

Integration tests use [Testcontainers](https://dotnet.testcontainers.org/) and require Docker running.

## Project Structure

```
src/
  WebhookEngine.Core/            # Domain: entities, enums, interfaces
  WebhookEngine.Infrastructure/   # EF Core, PostgreSQL queue, repositories
  WebhookEngine.Application/      # DI registration (CQRS scaffold — not yet active)
  WebhookEngine.Worker/           # Background services (delivery, retry, etc.)
  WebhookEngine.API/              # ASP.NET Core host, controllers, middleware
  WebhookEngine.Sdk/              # .NET SDK (NuGet package)
  dashboard/                      # React SPA (Vite + TypeScript + Tailwind)
tests/
  *.Tests/                        # xUnit test projects
samples/
  WebhookEngine.Sample.Sender/   # SDK usage demo
  WebhookEngine.Sample.Receiver/ # Webhook consumer with signature verification
  signature-verification/         # Copy-paste signature verification (C#, TS, Python)
```

## Code Style

### C# Backend

- PascalCase for classes, methods, properties
- `_camelCase` for private fields
- `Async` suffix on all async methods
- Constructor injection everywhere — no service locator pattern
- Pass `CancellationToken` through all async chains
- Use `IHttpClientFactory` — never `new HttpClient()`
- `AsNoTracking()` on read-only EF Core queries

### TypeScript Dashboard

- Yarn only (not npm or pnpm)
- Tailwind CSS v4 for styling
- Lucide React for icons
- ESLint + TypeScript strict mode

## Submitting Changes

### Issues

Before starting work on a feature or bug fix, check existing issues or open a new one to discuss the approach.

### Pull Requests

1. Fork the repository
2. Create a feature branch from `main` (`git checkout -b feature/my-change`)
3. Make your changes
4. Ensure the build passes: `dotnet build WebhookEngine.sln`
5. Ensure tests pass: `dotnet test WebhookEngine.sln`
6. Ensure the dashboard builds: `cd src/dashboard && yarn build`
7. Commit with a clear message describing the change
8. Open a pull request against `main`

### PR Guidelines

- Keep PRs focused — one feature or fix per PR
- Include relevant tests for new functionality
- Update documentation if you change public APIs
- Follow existing code style and naming conventions
- Add a description explaining **why** the change is needed, not just what changed

## Architecture Decisions

Key architectural constraints to keep in mind:

- **Single process**: API, workers, and dashboard are all served from one ASP.NET Core host
- **PostgreSQL only**: No Redis, RabbitMQ, or other dependencies for the core product
- **At-least-once delivery**: Messages may be delivered more than once but never lost
- **Standard Webhooks**: Signature format follows the [Standard Webhooks](https://www.standardwebhooks.com/) specification

For more details, see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
