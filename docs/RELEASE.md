# Release Guide

This guide explains how to publish:

1. Docker image to Docker Hub
2. .NET SDK package to NuGet

Both are automated by `.github/workflows/release.yml` and triggered by version tags (`v*`).

## Prerequisites

- GitHub repository with Actions enabled
- Docker Hub account and repository (`voyvodka/webhook-engine` by default)
- NuGet.org account with API key

## 1) Configure GitHub Secrets

In repository settings: **Settings -> Secrets and variables -> Actions**

Add these secrets:

| Secret | Description |
|--------|-------------|
| `DOCKERHUB_USERNAME` | Docker Hub username |
| `DOCKERHUB_TOKEN` | Docker Hub access token |
| `NUGET_API_KEY` | NuGet.org API key |

## 2) (Optional) Bootstrap labels and milestones

If you use the included scripts:

```bash
./scripts/setup-github-labels.sh voyvodka/webhook-engine
./scripts/setup-github-milestones.sh voyvodka/webhook-engine
```

These require `gh auth login` first.

## 3) Create a release tag

Tag format:

- Stable: `v1.0.0`
- Pre-release: `v1.0.0-preview.1`

`release.yml` automatically maps the tag to NuGet package version by stripping the `v` prefix.
Examples:

- `v1.0.0` -> `1.0.0`
- `v1.0.0-preview.1` -> `1.0.0-preview.1`

Push the tag to trigger publishing:

```bash
git tag v1.0.0
git push origin v1.0.0
```

## 4) What the workflow does

### Docker

- Logs in to Docker Hub
- Builds `docker/Dockerfile`
- Pushes image tags:
  - `voyvodka/webhook-engine:1.0.0` (from tag `v1.0.0`)
  - `voyvodka/webhook-engine:latest` (stable tags only)

For pre-release tags (e.g. `v1.0.0-preview.1`), only the version tag is pushed.

### NuGet

- Builds and packs `src/WebhookEngine.Sdk/WebhookEngine.Sdk.csproj`
- Uses the pushed git tag as `PackageVersion`
- Publishes generated `.nupkg` to `https://api.nuget.org/v3/index.json`
- Uses `--skip-duplicate` to avoid failures on re-runs

## 5) Verify the release

### Docker image

```bash
docker run --rm -p 5100:8080 voyvodka/webhook-engine:1.0.0
```

### NuGet package

```bash
cd SdkSmokeTest
dotnet add package WebhookEngine.Sdk --version 1.0.0
dotnet build
```

## Troubleshooting

### Docker publish failed

- Verify `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN`
- Ensure Docker Hub repo exists and account has push permission

### NuGet publish failed

- Verify `NUGET_API_KEY` is valid and not expired
- Confirm package ID/version is correct in `WebhookEngine.Sdk.csproj`

### Workflow did not trigger

- Ensure tag starts with `v`
- Confirm tag was pushed to origin
- Check Actions is enabled for the repository
