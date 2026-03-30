# ADR-001: Replay Scope Threat Model

**Status:** Accepted
**Date:** 2026-03-30
**Context:** Phase 02 Security Hardening (SECR-02)

## Context

WebhookEngine has two replay paths:

1. **API Replay** (`POST /api/v1/messages/replay`) — Authenticated via API key. The `AppId` is extracted from `HttpContext.Items["AppId"]` after `ApiKeyAuthMiddleware` validates the key. This endpoint is inherently AppId-scoped: an API key can only replay messages belonging to its own application. **No vulnerability here.**

2. **Dashboard Replay** — The `DashboardController` is authenticated via cookie-based admin session (`[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]`). The dashboard admin has full cross-application access by design. The dashboard `SendMessage` endpoint (`POST /api/v1/dashboard/messages/send`) accepts an explicit `AppId` in the request body, meaning an admin can send/replay messages for any application.

### Threat

If a dashboard admin credential is compromised, the attacker gains the ability to:
- Replay messages across any application
- Send new test messages to any application's endpoints
- View all application data, endpoints, and delivery history

### Risk Assessment

| Factor | Assessment |
|--------|------------|
| **Deployment model** | Self-hosted, single-tenant |
| **Admin user count** | Typically 1-3 operators per deployment |
| **Admin credential exposure** | Same risk as any admin panel — mitigated by HttpOnly cookies, SameSite=Lax, HTTPS in production |
| **Blast radius** | All applications in the single deployment |
| **Attack surface** | Requires valid admin session cookie — cannot be exploited via API key |

## Decision

**The current dashboard admin-only access model is sufficient for the self-hosted single-tenant deployment target. No backend guards are added at this time.**

Rationale:
1. WebhookEngine is designed as a self-hosted, single-tenant webhook delivery engine. The dashboard admin IS the deployment operator — full cross-app visibility is intentional, not a vulnerability.
2. The API replay path (used by external integrations) is already correctly AppId-scoped via API key authentication.
3. Adding per-app RBAC to the dashboard would introduce significant complexity (user-app mapping, role hierarchy, permission checks) for a threat that only materializes if the admin cookie is stolen — at which point the attacker already has full admin access regardless.
4. The cookie security is reasonable: HttpOnly (no XSS access), SameSite=Lax (no CSRF from cross-origin), 7-day sliding expiration.

### When to Revisit

This decision should be revisited if:
- WebhookEngine adds multi-tenant support (multiple organizations sharing one deployment)
- Dashboard user roles beyond "admin" are introduced
- A managed/hosted offering is planned (operator != tenant)

## Consequences

### Positive
- No additional code complexity in dashboard controllers
- No migration needed for user-app mapping tables
- Dashboard remains simple admin panel for single operator

### Negative
- Compromised admin cookie = full access to all applications (accepted risk for single-tenant)
- No audit trail for which admin replayed which messages (could be added later via logging)

### Neutral
- API replay path remains AppId-scoped — no change needed (per D-06)
- CONCERNS.md flag is resolved — threat is documented, risk is accepted with clear boundary conditions
