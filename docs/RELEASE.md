# Release Guide

This guide explains how to publish:

1. Docker image to Docker Hub
2. .NET SDK package to NuGet
3. `@webhookengine/endpoint-manager` npm package

Docker and the .NET SDK are automated by `.github/workflows/release.yml` and triggered by `v*` tags. The npm package is automated by `.github/workflows/publish-portal.yml` and triggered by `portal-v*` tags.

## Prerequisites

- GitHub repository with Actions enabled
- Docker Hub account and repository (`voyvodka/webhook-engine` by default)
- NuGet.org account with API key
- npmjs.com account with automation token (for the portal package)

## 1) Configure GitHub Secrets

In repository settings: **Settings -> Secrets and variables -> Actions**

Add these secrets:

| Secret | Description |
|--------|-------------|
| `DOCKERHUB_USERNAME` | Docker Hub username |
| `DOCKERHUB_TOKEN` | Docker Hub Personal Access Token. **Required scopes: `repo:read`, `repo:write`, `repo:write_metadata`.** The third scope is what lets the `Sync Docker Hub description` step in `release.yml` push the README to the Hub overview; without it that step fails with `403`. Create the token at Docker Hub → Account Settings → Personal Access Tokens. |
| `NUGET_API_KEY` | NuGet.org API key |
| `NPM_TOKEN` | npm automation token (for portal package — see Section 6) |

## 2) (Optional) Bootstrap labels and milestones

If you use the included scripts:

```bash
./scripts/setup-github-labels.sh voyvodka/webhook-engine
./scripts/setup-github-milestones.sh voyvodka/webhook-engine
```

These require `gh auth login` first.

## 3) Create a release tag

Tag format:

- Stable: `v0.1.0`
- Pre-release: `v0.1.1-preview.1`

`release.yml` automatically maps the tag to NuGet package version by stripping the `v` prefix.
Examples:

- `v0.1.0` -> `0.1.0`
- `v0.1.1-preview.1` -> `0.1.1-preview.1`

Push the tag to trigger publishing:

```bash
git tag v0.1.0
git push origin v0.1.0
```

## 4) What the workflow does

### Docker

- Logs in to Docker Hub
- Builds `docker/Dockerfile`
- Pushes image tags:
  - `voyvodka/webhook-engine:0.1.0` (from tag `v0.1.0`)
  - `voyvodka/webhook-engine:latest` (stable tags only)

For pre-release tags (e.g. `v0.1.1-preview.1`), only the version tag is pushed.

### NuGet

- Builds and packs `src/WebhookEngine.Sdk/WebhookEngine.Sdk.csproj`
- Uses the pushed git tag as `PackageVersion`
- Publishes generated `.nupkg` to `https://api.nuget.org/v3/index.json`
- Uses `--skip-duplicate` to avoid failures on re-runs

## 5) Verify the release

### Docker image

```bash
docker run --rm -p 5100:8080 voyvodka/webhook-engine:0.1.0
```

### NuGet package

```bash
cd SdkSmokeTest
dotnet add package WebhookEngine.Sdk --version 0.1.0
dotnet build
```

### Automated smoke (optional)

```bash
./scripts/release-smoke.sh http://localhost:5100 admin@example.com changeme
```

This verifies `/health`, `/metrics`, dashboard login/session, overview, timeline, and dev-traffic endpoint behavior.

## 6) Portal package release — @webhookengine/endpoint-manager

The portal npm package follows an **independent SemVer** track from the engine. The engine can ship `v0.2.x` while the portal package stays at `v0.1.x`, and vice versa. A `portal-v*` tag never triggers the Docker or NuGet jobs, and a `v*` engine tag never triggers the npm publish.

### 6.1 Tag pattern

```
portal-v{major}.{minor}.{patch}
portal-v0.1.0
portal-v0.1.1-beta.1   # pre-release
```

The workflow strips the `portal-v` prefix to extract the npm version: `portal-v0.1.0` → `0.1.0`.

### 6.2 NPM_TOKEN secret setup

1. Log in to [npmjs.com](https://www.npmjs.com).
2. Go to **Settings → Access Tokens → Generate New Token → Automation**.
   - Choose **Automation** (not Publish) so the token works in CI without 2FA prompts.
3. Copy the generated token (shown only once).
4. In the GitHub repository go to **Settings → Secrets and variables → Actions → New repository secret**.
5. Name: `NPM_TOKEN`, value: the token you copied.

The `publish-portal.yml` workflow consumes it as `NODE_AUTH_TOKEN` — this is the conventional env var name that `actions/setup-node` wires to the npm registry automatically.

### 6.3 Provenance / sigstore

The workflow passes `--provenance` to `npm publish`. This attaches a sigstore attestation to the package so consumers can verify its build provenance via `npm audit signatures`. The `id-token: write` permission in the workflow grants the GitHub Actions OIDC token that sigstore requires. No additional setup is needed beyond the permission already declared in the workflow file.

### 6.4 Pre-flight checklist before pushing a portal-v* tag

Run these steps on the release branch before tagging:

1. **Bump the version** in `packages/endpoint-manager/package.json` to match the intended tag. The workflow also runs `npm version` defensively, but having a clean source-of-truth value in `package.json` keeps git history readable.

2. **Set `private: false`** (or remove the `private` field entirely). The package defaults to `private: true` during development to prevent accidental publishes. The workflow has an explicit guard that fails with a clear error message if `private: true` is still set when a `portal-v*` tag is pushed.

3. **Update `packages/endpoint-manager/CHANGELOG.md`** with the release notes for this version (created at Step 11 of the B1 roadmap).

4. **Verify the build artifacts** locally:

   ```bash
   cd packages/endpoint-manager
   bun run build
   ls -la dist/
   # Must contain: index.js  index.d.ts  style.css
   ```

5. **Run the pre-publish guard** locally:

   ```bash
   bun run typecheck
   bun run lint
   bun run test
   ```

6. Push the branch, open a PR, and let CI green before tagging.

7. After the PR merges to `main`, create and push the tag:

   ```bash
   git tag portal-v0.1.0
   git push origin portal-v0.1.0
   ```

### 6.5 Verify the npm publish

```bash
npm info @webhookengine/endpoint-manager
npm install @webhookengine/endpoint-manager@0.1.0
```

Check the package page on npmjs.com for the provenance badge (a green shield icon next to the version number).

## Troubleshooting

### Docker publish failed

- Verify `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN`
- Ensure Docker Hub repo exists and account has push permission

### NuGet publish failed

- Verify `NUGET_API_KEY` is valid and not expired
- Confirm package ID/version is correct in `WebhookEngine.Sdk.csproj`

### npm publish failed — private:true error

The workflow guard caught `private: true` in `packages/endpoint-manager/package.json`. Set `"private": false` (or remove the field) on the release branch and re-push the tag.

### npm publish failed — authentication error

Verify `NPM_TOKEN` secret is set and the token has not expired. Automation tokens do not expire by default but can be revoked manually on npmjs.com.

### Workflow did not trigger

- Engine workflow: ensure tag starts with `v` (not `portal-v`)
- Portal workflow: ensure tag starts with `portal-v`
- Confirm tag was pushed to origin
- Check Actions is enabled for the repository
