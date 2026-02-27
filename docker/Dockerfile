# ============================================================
# Stage 1: Dashboard build (React SPA)
# ============================================================
FROM node:20-alpine AS dashboard-build
WORKDIR /dashboard

COPY src/dashboard/package.json src/dashboard/yarn.lock ./
RUN yarn install --frozen-lockfile

COPY src/dashboard/ .
RUN yarn build

# ============================================================
# Stage 2: .NET restore + publish
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src

# Copy solution and project files for layer-cached restore
COPY WebhookEngine.sln .
COPY src/WebhookEngine.Core/WebhookEngine.Core.csproj src/WebhookEngine.Core/
COPY src/WebhookEngine.Infrastructure/WebhookEngine.Infrastructure.csproj src/WebhookEngine.Infrastructure/
COPY src/WebhookEngine.Application/WebhookEngine.Application.csproj src/WebhookEngine.Application/
COPY src/WebhookEngine.Worker/WebhookEngine.Worker.csproj src/WebhookEngine.Worker/
COPY src/WebhookEngine.API/WebhookEngine.API.csproj src/WebhookEngine.API/
COPY src/WebhookEngine.Sdk/WebhookEngine.Sdk.csproj src/WebhookEngine.Sdk/

# Restore only the deployable host project.
# Restoring the full solution requires test/sample project files in the image
# and breaks Docker layer-cached restore when they are intentionally omitted.
RUN dotnet restore src/WebhookEngine.API/WebhookEngine.API.csproj

# Copy source and publish
COPY src/ src/

# Copy dashboard build output into wwwroot before publish.
# Vite outDir is configured as ../WebhookEngine.API/wwwroot, which resolves
# to /WebhookEngine.API/wwwroot inside the dashboard-build stage.
COPY --from=dashboard-build /WebhookEngine.API/wwwroot/ src/WebhookEngine.API/wwwroot/

RUN dotnet publish src/WebhookEngine.API/WebhookEngine.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ============================================================
# Stage 3: Runtime
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

# Labels
LABEL org.opencontainers.image.title="WebhookEngine" \
      org.opencontainers.image.description="Self-hosted webhook delivery platform" \
      org.opencontainers.image.url="https://github.com/voyvodka/webhook-engine" \
      org.opencontainers.image.source="https://github.com/voyvodka/webhook-engine" \
      org.opencontainers.image.licenses="MIT"

# Security: run as non-root user
RUN addgroup -S appgroup && adduser -S appuser -G appgroup

WORKDIR /app
COPY --from=backend-build --chown=appuser:appgroup /app/publish .

USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "WebhookEngine.API.dll"]
