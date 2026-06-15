# ============================================================
# Stage 1: Dashboard build (React SPA)
# ============================================================
# Image digests pinned so Dependabot can track and bump them.
FROM oven/bun:1-alpine@sha256:5acc90a93e91ff07bf72aa90a7c9f0fa189765aec90b47bdbf2152d2196383c0 AS dashboard-build
WORKDIR /workspace

# Workspace plumbing: install from the root so Bun resolves the workspace
# graph correctly. Only the package manifests are copied for layer caching.
COPY package.json bun.lock ./
COPY src/dashboard/package.json src/dashboard/package.json
COPY packages/endpoint-manager/package.json packages/endpoint-manager/package.json
COPY samples/portal-host/package.json samples/portal-host/package.json
RUN bun install --frozen-lockfile

COPY src/dashboard src/dashboard
WORKDIR /workspace/src/dashboard
RUN bun run build

# ============================================================
# Stage 2: .NET restore + publish
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:c0790639332692a0d56cdd81ed581cfd24d040d9839764c138994866df89a3b6 AS backend-build
WORKDIR /src

# Copy solution and project files for layer-cached restore
COPY WebhookEngine.sln .
COPY src/WebhookEngine.Core/WebhookEngine.Core.csproj src/WebhookEngine.Core/
COPY src/WebhookEngine.Infrastructure/WebhookEngine.Infrastructure.csproj src/WebhookEngine.Infrastructure/
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
# to /workspace/src/WebhookEngine.API/wwwroot inside the dashboard-build stage.
COPY --from=dashboard-build /workspace/src/WebhookEngine.API/wwwroot/ src/WebhookEngine.API/wwwroot/

RUN dotnet publish src/WebhookEngine.API/WebhookEngine.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ============================================================
# Stage 3: Runtime
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:f03685b2735e0d3d25d6c60672e74b21bb6334f1402f71bae2d2cf02307163cd AS runtime

# Labels
LABEL org.opencontainers.image.title="WebhookEngine" \
      org.opencontainers.image.description="Self-hosted webhook delivery platform" \
      org.opencontainers.image.url="https://github.com/voyvodka/webhook-engine" \
      org.opencontainers.image.source="https://github.com/voyvodka/webhook-engine" \
      org.opencontainers.image.documentation="https://webhook.sametozkan.com.tr" \
      org.opencontainers.image.vendor="WebhookEngine" \
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
