# ADR-005: Portal CORS Preflight Deny-Cache — TTL Choice

**Status:** Accepted
**Date:** 2026-05-11
**Context:** v0.2.0 post-merge audit, P1 finding #4 (PR #104 / Tur 4)

## Context

`PortalCorsMiddleware.HandlePreflightAsync` resolves whether an `OPTIONS` request from a given `Origin` should be allowed by scanning the portal-enabled application set:

```csharp
var allowed = await appRepo.AnyAllowsPortalOriginAsync(origin, ct);
```

`ApplicationRepository.AnyAllowsPortalOriginAsync` loads every portal-enabled app's `AllowedPortalOriginsJson` blob, deserializes each, and walks the lists for an exact (case-insensitive) match.

The audit's P1 finding flagged that:

1. **Browsers do not cache rejected preflight responses.** A `403` on `OPTIONS` triggers a fresh preflight on the next attempt — there is no `Access-Control-Max-Age` semantics for a denial.
2. **Therefore every disallowed `OPTIONS /api/v1/portal/*` triggered a fresh DB scan.** An attacker (or a misconfigured browser) hitting the portal prefix from an unauthorized origin in a tight loop would amplify into a per-app, per-origin DB sweep — a free DoS vector.

The fix is to cache the preflight decision (allow **and** deny) server-side. The remaining question is the TTL.

## Decision

Cache both allow and deny outcomes via the existing `IMemoryCache` for **`PortalAuth:LookupCacheTtlSeconds`** (default **60s**). The cache key is the lowercased origin:

```csharp
var cacheKey = $"portal:cors:{origin.ToLowerInvariant()}";
```

No invalidation hook is wired from operator mutating actions (`PUT /portal/origins`, `POST /portal/disable`, etc.). Operators rely on TTL-bounded eventual consistency for CORS preflight decisions.

## Consequences

### Positive

- **Closes the DB-hammer vector.** A flood of disallowed `OPTIONS` from a single origin now hits the DB at most once per TTL window per origin, instead of once per request. The candidate JSON deserialization loop runs accordingly less often.
- **Symmetry with the per-app signing-key cache.** Both portal-related caches share `PortalAuth:LookupCacheTtlSeconds`. Operators have one knob to turn if they need shorter freshness windows; the mental model stays simple.
- **Allow-cache also helps the legitimate case.** A real customer browsing the embedded portal performs at least one `OPTIONS` per cross-origin request the browser hasn't already cached for the route — caching the allow outcome reduces per-customer DB pressure too.

### Negative

- **Origin allowlist updates take up to 60s to take effect.** If an operator removes an origin from `PUT /portal/origins`, requests from that origin may continue to receive a `204` preflight response for up to a minute — a stale allow. This is the same eventual-consistency trade the per-app signing-key cache already makes; it is not a new behaviour, just a new surface.
- **Stale deny is also possible.** If an operator *adds* an origin, requests from it may continue to receive `403` for up to a minute. Same TTL bound.

### Neutral

- **Multi-replica deployments were already bounded by cache TTL** for the signing-key lookup. CORS now follows the same pattern; nothing about the multi-replica behaviour gets worse.
- **The cache is local-process.** A multi-replica deployment that rotates origins frequently will see per-replica drift up to 60s. This matches the rest of the portal cache surface — no additional invariant is introduced.

## Alternatives considered

- **Wire an invalidation hook from `PUT /portal/origins` and `POST /portal/disable`.** Rejected for v0.2.x: the cache key is *origin-scoped*, not app-scoped, but operator actions act on the *app's* allowlist. Invalidating "all CORS cache entries that might have referenced this app's allowlist" requires either a full-cache flush (noisy across all apps) or a secondary index (added complexity for a problem the TTL already bounds). The cost of being slightly stale for ≤60s is lower than the cost of either workaround.
- **Shorter TTL (e.g. 10s).** Rejected: the DB-hammer mitigation strength scales linearly with the TTL. 60s is already the same window we accept for signing-key freshness. Shorter TTLs reduce the security gain without buying meaningful operator visibility.
- **Longer TTL (e.g. 5 min).** Rejected: makes operator origin updates feel laggy in the dashboard, and the 60s symmetry with the signing-key cache becomes asymmetric noise.
- **Negative-only cache (cache deny, never allow).** Rejected: half-solves the problem. The deny-cache stops the abuse case, but the allow-cache helps the legitimate case symmetrically — there's no reason to skip it.

## Revisit triggers

This decision should be reopened if any of the following becomes true:

- An operator reports that origin allowlist changes "don't take effect" because their support flow needs immediate consistency (e.g. a customer is locked out and waiting on hold while the cache TTL elapses). The fix would be the synchronous-invalidation hook described under Alternatives — moderately complex but well-scoped.
- The CORS deny-cache is observed leaking memory in long-running deployments (origin enumeration attack — attacker rotates `Origin` to balloon the cache). Mitigation: bounded-size cache entries + LRU eviction, or per-app CORS prefix scoping.
- The portal layer adds richer CORS semantics (multiple `Allow-Methods` per app, per-route allowlists, etc.) where origin-scoped caching no longer fits.
