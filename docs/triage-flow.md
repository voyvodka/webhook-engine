# Post-Release Triage Flow

Triage process for WebhookEngine post-release issue management.

## Overview

Every new issue goes through triage before work begins. Triage assigns reproducibility status, component label, and priority. Issues without triage labels are not scheduled.

## Issue Lifecycle

```
New -> Triaged -> In Progress -> Done
```

- **New:** Issue created, no labels assigned
- **Triaged:** Reproducibility, component, and priority assigned
- **In Progress:** Assigned to a release milestone, work started
- **Done:** Fix merged and verified

## Reproducibility Classification

Every bug report must be classified:

| Classification | Label | Meaning |
|----------------|-------|---------|
| Confirmed | `repro:confirmed` | Reproduced locally with steps |
| Intermittent | `repro:intermittent` | Occurs inconsistently, not always reproducible |
| Cannot Reproduce | `repro:cannot-reproduce` | Unable to reproduce with provided steps |

Cannot-reproduce issues stay open for 14 days awaiting additional information, then close.

## Component Labels

Assign exactly one component label per issue:

| Label | Scope |
|-------|-------|
| `component:delivery` | Message delivery pipeline, retry logic, dead lettering |
| `component:circuit-breaker` | Circuit breaker state machine, endpoint health tracking |
| `component:dashboard` | React dashboard, SignalR live feed, dashboard API endpoints |
| `component:api` | Public REST API endpoints, authentication, rate limiting |
| `component:sdk` | .NET SDK client library |
| `component:infra` | Docker, deployment, database migrations, configuration |
| `component:docs` | Documentation, ADRs, process docs |

## Priority Assignment

Assign exactly one priority label per issue:

| Priority | Label | Definition | Response Time |
|----------|-------|------------|---------------|
| P0 | `priority:p0` | Data loss, security vulnerability, or complete service outage | Immediate — next patch release |
| P1 | `priority:p1` | Core delivery function broken, no workaround | Fix within current milestone |
| P2 | `priority:p2` | Feature degraded but workaround exists | Schedule in next milestone |
| P3 | `priority:p3` | Cosmetic, enhancement, or minor inconvenience | Backlog — address when capacity allows |

**Escalation rule:** Any P0 issue triggers an immediate patch release discussion. P0 and P1 issues are always candidates for the next patch release.

## Triage Checklist

For each new issue:

1. [ ] Read issue description and reproduction steps
2. [ ] Attempt local reproduction (if bug report)
3. [ ] Assign reproducibility label
4. [ ] Assign component label
5. [ ] Assign priority label
6. [ ] Add to milestone (if P0 or P1)
7. [ ] Comment with triage summary

## Weekly Triage Cadence

- Review all unlabeled issues weekly
- Re-assess `repro:cannot-reproduce` issues older than 14 days
- Promote P2 issues to P1 if workaround is no longer viable
- Close stale issues (no activity for 30 days, cannot reproduce)
