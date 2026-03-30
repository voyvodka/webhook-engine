# TypeScript SDK — Demand Validation Criteria

**Status:** Proposed
**Date:** 2026-03-30
**Context:** Phase 04 Performance & Future Decisions (GHIS-04, GitHub #4)
**Deferred to:** v0.2.0 (see docs/backlog-v0.1.1.md)

## Background

WebhookEngine ships a .NET SDK (`WebhookEngine.Sdk`) for .NET consumers. A TypeScript/Node.js SDK would expand the consumer base to JavaScript/TypeScript applications. Before investing in building the SDK, demand must be validated against concrete metrics.

This document defines the go/no-go criteria for building a TypeScript SDK. The decision is deferred until evidence meets the thresholds below.

## Go/No-Go Criteria

The decision to build a TypeScript SDK requires **at least 2 of 3** metric groups to meet their "Go" threshold.

### Metric 1: Community Demand

| Signal | Go Threshold | Source |
|--------|-------------|--------|
| Distinct GitHub users requesting SDK | >= 5 distinct users | GitHub issues, discussions, and comments mentioning "TypeScript SDK", "Node.js SDK", or "JS client" |
| GitHub issue reactions (thumbs up) on SDK tracking issue | >= 10 reactions | GitHub #4 reaction count |
| External references (blog posts, forum threads) | >= 2 independent references | Google search, Stack Overflow, Reddit |

**Measurement:** Count distinct GitHub usernames across all SDK-related issues/discussions. Do not double-count the same user across signals.

### Metric 2: .NET SDK Usage Evidence

| Signal | Go Threshold | Source |
|--------|-------------|--------|
| Known deployments using .NET SDK | >= 3 deployments | GitHub issues mentioning SDK usage, Docker Hub pulls with SDK-dependent images |
| SDK-related bug reports or feature requests | >= 2 issues | GitHub issues tagged with SDK label |

**Rationale:** If the .NET SDK has low adoption, a TypeScript SDK may face the same fate. Evidence of .NET SDK usage validates the "SDK consumer" pattern.

### Metric 3: Ecosystem Assessment

| Signal | Go Threshold | Source |
|--------|-------------|--------|
| Competitor webhook platforms offering TypeScript/Node.js SDKs | >= 2 competitors | Svix, Hookdeck, Convoy SDK documentation |
| Target audience TypeScript density | >= 40% of webhook consumers use Node.js/TS | npm download trends for webhook-related packages, State of JS survey data |

**Rationale:** If the ecosystem has moved to TypeScript-first webhook consumption, not offering an SDK is a competitive disadvantage.

## Decision Matrix

| Metrics Met | Decision |
|-------------|----------|
| 3 of 3 | **Go** — prioritize in next milestone |
| 2 of 3 | **Go** — schedule in backlog with medium priority |
| 1 of 3 | **No-Go** — re-evaluate in 6 months |
| 0 of 3 | **No-Go** — close issue, revisit only if demand surfaces |

## SDK Scope (if Go)

If the decision is "Go", the TypeScript SDK should mirror the .NET SDK scope:

1. **Core operations:** Send message, batch send, list messages, replay message
2. **Application management:** Create/list/update applications
3. **Endpoint management:** Create/list/update/delete endpoints
4. **Authentication:** API key-based (same as .NET SDK)
5. **Package:** Published to npm as `@webhook-engine/sdk` or `webhook-engine-sdk`
6. **Runtime:** Node.js 18+ and browser (fetch-based)

## Next Steps

1. Create a GitHub discussion or issue specifically for TypeScript SDK interest tracking
2. Add a "sdk" label to the GitHub repository for filtering
3. Review metrics at each milestone boundary (v0.2.0, v0.3.0, etc.)
4. If Go: create a dedicated milestone for SDK implementation
