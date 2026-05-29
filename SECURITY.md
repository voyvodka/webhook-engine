# Security Policy

## Supported Versions

WebhookEngine is currently in pre-1.0 development. Security fixes are applied
only to the **latest minor release line**. Older minor releases do not receive
backported patches.

| Version | Supported |
|---------|-----------|
| 0.2.x (latest) | Yes — security fixes applied |
| 0.1.x and earlier | No — please upgrade to 0.2.x |

Once the project reaches v1.0.0 this table will be updated to reflect the
long-term support policy.

## Reporting a Vulnerability

**Please do not open a public GitHub issue for security vulnerabilities.**
Public disclosure before a fix is available puts all self-hosted deployments
at risk.

Use **GitHub's private vulnerability reporting** instead:

1. Go to the [Security tab](https://github.com/voyvodka/webhook-engine/security)
   of this repository.
2. Click **"Report a vulnerability"**.
3. Fill in the advisory form with as much detail as possible — reproduction
   steps, affected versions, and any proof-of-concept.

We aim to send an **initial acknowledgement within 5 business days**. The full
remediation timeline depends on severity and complexity, but we will keep you
updated throughout the process.

If GitHub's private reporting flow is unavailable for any reason, open a
[blank issue](https://github.com/voyvodka/webhook-engine/issues/new) with the
title `[SECURITY] private contact requested` and we will reach out through an
alternative channel.

## Scope

The following are in scope for vulnerability reports:

- **WebhookEngine server** — the `.NET 10` API, worker, and infrastructure
  layers (`src/WebhookEngine.*/`).
- **WebhookEngine.Sdk** — the .NET client NuGet package
  (`src/WebhookEngine.Sdk/`).
- **`@webhookengine/endpoint-manager`** — the embeddable React package
  (`packages/endpoint-manager/`).
- **Official Docker image** — `voyvodka/webhook-engine` on Docker Hub.

The following are **out of scope**: third-party dependencies (report those
upstream), the landing-site static pages, and self-hosted infrastructure
operated by end users.

## Preferred Languages

English or Turkish are both fine for vulnerability reports.
