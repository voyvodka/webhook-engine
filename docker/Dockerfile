# ============================================================
# Stage 1: Dashboard build (React SPA)
# ============================================================
# Image digests pinned for reproducible builds + Dependabot-tracked security
# updates. The mutable tag (oven/bun:1-alpine, dotnet/sdk:10.0,
# dotnet/aspnet:10.0-alpine) is kept alongside so Dependabot can detect when
# a newer digest is published and open an automatic bump PR.
FROM oven/bun:1-alpine@sha256:4de475389889577f346c636f956b42a5c31501b654664e9ae5726f94d7bb5349 AS dashboard-build
WORKDIR /dashboard

COPY src/dashboard/package.json src/dashboard/bun.lock ./
RUN bun install --frozen-lockfile

COPY src/dashboard/ .
RUN bun run build

# ============================================================
# Stage 2: .NET restore + publish
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:8a90a473da5205a16979de99d2fc20975e922c68304f5c79d564e666dc3982fc AS backend-build
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
# to /WebhookEngine.API/wwwroot inside the dashboard-build stage.
COPY --from=dashboard-build /WebhookEngine.API/wwwroot/ src/WebhookEngine.API/wwwroot/

RUN dotnet publish src/WebhookEngine.API/WebhookEngine.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ============================================================
# Stage 3: Runtime
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:60eb031b554df75a4b9f358290a2fa15d8961a3bc79b47bb34a00e31f7b78c69 AS runtime

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
