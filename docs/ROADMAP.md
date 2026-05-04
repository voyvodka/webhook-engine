# Roadmap
# WebhookEngine — Strategic Roadmap

**Last Updated:** 2026-05-04
**Status:** Active — Phase 2 (Traction & Feedback)

> **Note:** Phase 1 is complete (launch posts and the engineering blog post remain deferred). Phase 2 core tasks (2.2, 2.3, 2.4) are done. Remaining Phase 2 items (payload transformation, TypeScript SDK, application layer cleanup) are planned.

---

## Vision

WebhookEngine is the first product in a PostgreSQL-native, MIT-licensed SaaS infrastructure toolkit. Starting as a focused webhook delivery platform, it evolves into a multi-channel notification infrastructure — all running from a single Docker Compose.

```
Phase 1          Phase 2          Phase 3              Phase 4
(Month 1-2)      (Month 2-4)      (Month 4-8)          (Month 8-12)
━━━━━━━━━━━      ━━━━━━━━━━━      ━━━━━━━━━━━━━        ━━━━━━━━━━━━━
Launch &         Traction &       Notification          Multi-Tenant
First Users      Feedback         Infrastructure        Identity Library
```

---

## Phase 1: Launch & First Users (Month 1-2)

**Goal:** Public GitHub repo, Docker Hub image, first external users.

**Principle:** Ship what we have. No new features — only polish, docs, and launch.

### Tasks

| # | Task | Priority | Est. | Status |
|---|------|----------|------|--------|
| 1.1 | Sample app: ASP.NET Core sender + webhook receiver | P0 | 2d | done |
| 1.2 | Complete SDK (WebhookEngineClient real methods) | P0 | 2d | done |
| 1.3 | Signature verification helpers (C#, TypeScript, Python) | P0 | 1d | done |
| 1.4 | Getting Started guide (zero to first webhook in 5 min) | P0 | 1d | done |
| 1.5 | Self-hosting guide (config reference, production tips) | P1 | 1d | done |
| 1.6 | CONTRIBUTING.md + issue/PR templates | P1 | 0.5d | done |
| 1.7 | GitHub repo setup (labels, milestones, release) | P1 | 0.5d | prepared |
| 1.8 | Docker Hub publish (voyvodka/webhook-engine:latest) | P0 | 0.5d | done |
| 1.9 | NuGet publish (WebhookEngine.Sdk) | P0 | 0.5d | done |
| 1.10 | Launch posts (HN Show HN, r/dotnet, r/selfhosted) | P0 | 1d | deferred |
| 1.11 | Blog: "How We Built Reliable Webhook Delivery with PostgreSQL" | P1 | 1d | deferred |

### Success Criteria
- [ ] `docker compose up` → working system in < 2 minutes
- [ ] GitHub repo public with README, docs, LICENSE, CONTRIBUTING
- [x] Docker Hub image available
- [x] NuGet SDK published
- [ ] At least 1 launch post published
- [x] Sample app works end-to-end

### What NOT to do
- No new features
- No TypeScript SDK yet (wait for demand signal)
- No Kubernetes support
- No UI redesign

---

## v0.1.3 — Presence & Packaging (2026-04-09)

Patch release focused on project discoverability and packaging hygiene. No new engine features.

| # | Task | Status |
|---|------|--------|
| P.1 | Landing page at [webhook.sametozkan.com.tr](https://webhook.sametozkan.com.tr) — features, quick start, links | done |
| P.2 | GitHub Pages setup with custom domain + SSL | done |
| P.3 | SEO: JSON-LD structured data, OG/Twitter tags, canonical, sitemap, robots.txt | done |
| P.4 | SDK version aligned with main project (`0.1.3`), `PackageProjectUrl` updated | done |
| P.5 | GitHub repo homepage set to landing page | done |
| P.6 | README header updated with website, Docker Hub, NuGet links | done |
| P.7 | Internal planning docs removed from public repo | done |

---

## Phase 2: Traction & Feedback (Month 2-4)

**Goal:** Learn from real users. Fix what's broken. Add what's missing.

**Principle:** Listen first, build second. Every feature must come from user feedback or clear demand signal.

### Expected Tasks (driven by feedback)

| # | Task | Priority | Trigger | Status |
|---|------|----------|---------|--------|
| 2.1 | Bug fixes and edge cases | P0 | GitHub issues | in_progress |
| 2.2 | Event replay (re-deliver events in a time range) | P1 | Almost certain user request | done |
| 2.3 | Batch message sending (multiple events in one API call) | P1 | API power users | done |
| 2.4 | Rate limiting per endpoint | P1 | Large-scale users | done |
| 2.5 | Webhook payload transformation (modify before delivery) | P2 | Integration use cases | planned |
| 2.6 | TypeScript SDK (npm) | P1 | If demand signal exists | planned |
| 2.7 | Application layer cleanup (implement CQRS or remove scaffold) | P1 | Tech debt | planned |
| 2.8 | Blog: "Webhook Delivery Best Practices" | P1 | SEO + authority | planned |
| 2.9 | Blog: "WebhookEngine vs Svix vs Convoy" | P1 | SEO + positioning | planned |
| 2.10 | Integration guide: ABP Framework + WebhookEngine | P2 | .NET ecosystem reach | planned |

### Decision Point (end of Phase 2)

Evaluate based on data:
- Docker pulls > 500? → Market exists, proceed to Phase 3
- GitHub issues active? → Users are engaged
- Zero traction? → Reassess positioning or pivot

### Success Criteria
- [ ] 500+ Docker Hub pulls
- [ ] 200+ GitHub stars
- [ ] At least 5 GitHub issues from external users
- [ ] 2+ blog posts published

---

## Phase 3: Notification Infrastructure (Month 4-8)

**Goal:** Evolve WebhookEngine from webhook-only to multi-channel notification platform.

**Principle:** Webhook remains the core. New channels are additive. Existing users are not disrupted.

### Why expand (not new project)
- Same codebase, same community, same Docker image
- Webhook is just one notification channel — email, SMS, push are others
- Novu (38.6K stars) proves the market; their weakness (MongoDB + Redis) is our strength
- "PostgreSQL-native Novu alternative" is a stronger story than "Svix alternative in .NET"

### Architecture Evolution

```
WebhookEngine v1 (current)        WebhookEngine v2 (Phase 3)
━━━━━━━━━━━━━━━━━━━━━━━━━         ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
                                   INotificationChannel interface
Webhook delivery (HTTP POST)  →    ├── WebhookChannel (existing)
                                   ├── EmailChannel (SMTP, SendGrid, Resend)
                                   ├── InAppChannel (SignalR real-time)
                                   └── (future: SMS, Push)

                                   Subscriber management
                                   ├── Subscriber profiles
                                   ├── Channel preferences
                                   └── Topic subscriptions

                                   Notification workflows
                                   ├── Send → Delay → Send
                                   ├── Channel fallback
                                   └── Digest/batching
```

### Tasks

| # | Task | Priority | Est. |
|---|------|----------|------|
| 3.1 | `INotificationChannel` interface + refactor webhook as first channel | P0 | 3d |
| 3.2 | Email channel: SMTP provider | P0 | 3d |
| 3.3 | Email channel: SendGrid provider | P1 | 2d |
| 3.4 | Email channel: Resend provider | P2 | 1d |
| 3.5 | Subscriber entity + management API | P0 | 3d |
| 3.6 | Subscriber channel preferences | P0 | 2d |
| 3.7 | In-App notification channel (SignalR) | P0 | 4d |
| 3.8 | `<NotificationInbox />` React component (embeddable) | P1 | 5d |
| 3.9 | Notification workflow engine (send → delay → send) | P1 | 5d |
| 3.10 | Channel fallback (try push, fallback to email) | P2 | 3d |
| 3.11 | Digest/batching ("group last N events into one email") | P2 | 3d |
| 3.12 | Template system (Handlebars-style variables, per-channel) | P1 | 3d |
| 3.13 | Dashboard: subscriber management page | P0 | 3d |
| 3.14 | Dashboard: notification log (all channels) | P0 | 2d |
| 3.15 | Dashboard: workflow editor (visual) | P2 | 5d |
| 3.16 | npm package: `@webhookengine/inbox` | P1 | 2d |
| 3.17 | Documentation: notification channels guide | P0 | 2d |
| 3.18 | Blog: "PostgreSQL-Native Notification Infrastructure" | P1 | 1d |

### Success Criteria
- [ ] Email + In-App + Webhook channels working
- [ ] Subscriber preferences functional
- [ ] `<NotificationInbox />` npm package published
- [ ] Positioning shift: "webhook + notification infrastructure"
- [ ] 1000+ GitHub stars

---

## Phase 4: Multi-Tenant Identity Library (Month 8-12)

**Goal:** Separate NuGet package for ASP.NET Core multi-tenant Identity.

**Principle:** Independent project, same brand ecosystem. WebhookEngine dogfoods it for its own multi-tenant support.

### Scope (from 01-multi-tenant-identity.md)

| # | Task | Priority |
|---|------|----------|
| 4.1 | TenantIdentityDbContext (extends IdentityDbContext) | P0 |
| 4.2 | TenantUserManager (tenant-scoped queries) | P0 |
| 4.3 | TenantSignInManager (tenant resolution during login) | P0 |
| 4.4 | User-Tenant many-to-many mapping | P0 |
| 4.5 | Multi-DB migration engine (ITenantMigrator) | P0 |
| 4.6 | Tenant resolution strategies (subdomain, path, header) | P0 |
| 4.7 | Finbuckle compatibility layer | P1 |
| 4.8 | Sample project + documentation | P0 |
| 4.9 | NuGet publish | P0 |

### Success Criteria
- [ ] NuGet package published
- [ ] Works with PostgreSQL + SQL Server
- [ ] Sample project with WebhookEngine integration
- [ ] 2000+ NuGet downloads in first 3 months

---

## Long-Term Vision (Month 12+)

```
WebhookEngine Ecosystem
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
WebhookEngine            → Webhook + Notification delivery
  + Email, SMS, Push, In-App channels
  + Workflow orchestration
  + Embeddable <NotificationInbox />

TenantIdentity           → Multi-tenant ASP.NET Core Identity
  + Shared Identity DB + per-tenant data DB
  + NuGet package

(possible) EventBus      → PostgreSQL-native event bus
  + Pub/sub between microservices
  + Outbox pattern built-in

All: PostgreSQL-only, Docker Compose, MIT licensed, .NET native.
```

### Potential features (unscheduled)
- SMS channel (Twilio, Vonage)
- Push channel (FCM, APNs)
- WhatsApp Business API
- Managed cloud offering (SaaS)
- Kubernetes Helm chart
- Embeddable endpoint management portal
- A/B testing for notification content
- Analytics (delivery rate, open rate, click rate)
- Svix-compatible API (easy migration)

---

## Metrics Dashboard

| Metric | Phase 1 | Phase 2 | Phase 3 | Phase 4 |
|--------|---------|---------|---------|---------|
| GitHub stars | 50+ | 200+ | 1000+ | 1500+ |
| Docker pulls | 100+ | 500+ | 5000+ | 10000+ |
| NuGet downloads | 50+ | 500+ | 2000+ | 5000+ |
| Contributors | 1 | 3+ | 10+ | 15+ |
| Blog posts | 1 | 3+ | 5+ | 7+ |
