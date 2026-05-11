# ADR-004: Portal Signing Key Storage — Plaintext + Instant Invalidation, No Rotation Grace

**Status:** Accepted
**Date:** 2026-05-11
**Context:** B1 Embeddable Customer Portal (v0.2.0); v0.2.0 post-merge audit (PRs #101–#104)

## Context

The embeddable customer portal authenticates `/api/v1/portal/*` requests with HS256 JWTs minted by the host SaaS using a per-application signing key. The engine never mints these tokens — it only verifies them. Two storage and lifecycle decisions had to be locked in before v0.2.1:

1. **At-rest storage** of the per-app `PortalSigningKey`.
2. **Rotation lifecycle** — whether a rotated key has a grace window during which both the old and new keys verify.

The v0.2.0 audit (specifically the reviewer agent's P2 finding #10) flagged both as worth recording as ADRs because the consequences are not obvious from the code alone, and the choices are likely to be revisited.

## Decision

### 1. Plaintext storage in `applications.portal_signing_key` (`varchar(64)`)

The signing key is stored in plaintext alongside the existing `signing_secret` column. No application-level encryption-at-rest, no `pgcrypto`, no envelope encryption. At-rest protection relies entirely on the operator's Postgres deployment posture (filesystem-level encryption, restricted backups, network isolation).

### 2. Instant invalidation on rotate / disable, with **no grace period**

When the operator calls `POST /portal/rotate` or `POST /portal/disable`:

- The new (or null) value is written to `applications.portal_signing_key`.
- `PortalLookupCache.InvalidateApplication(appId)` is called synchronously on the local node — within milliseconds, the next portal request validates against the new key.
- On remote replicas, the change becomes effective within `PortalAuth:LookupCacheTtlSeconds` (default 60s).
- **Tokens minted with the old key fail verification immediately** (no overlapping validity window).

### 3. One-shot reveal

`POST /portal/enable` and `POST /portal/rotate` return the freshly generated key in the response body **once**. Subsequent reads (`GET /portal`) return only `portalEnabled: bool`, the rotated-at timestamp, and the allowed-origins list — never the key itself. The audit log records the rotate action with `portalEnabled: bool` instead of the literal secret.

## Consequences

### Positive

- **Operational simplicity.** Existing `SigningSecret` column already follows the same pattern; a single storage convention covers both auth surfaces. No new key-management subsystem to maintain.
- **Instant revoke.** A leaked or suspected-leaked key can be killed in one click — no minutes-long window during which a stolen token still works. For a self-hosted webhook engine where the operator is the security boundary, this is the right default.
- **Audit clarity.** The audit log entry's `before/after` snapshot redacts the signing key to a boolean, so audit trails never carry secrets — operators can grant audit-log access broadly without leaking auth material.
- **Matches existing convention.** `SigningSecret` (per-app HMAC delivery secret) is also plaintext in `applications.signing_secret`. Diverging here would create asymmetry without solving the underlying threat (DB compromise compromises both).

### Negative

- **Database compromise = key exposure.** An attacker with read access to the `applications` table (e.g. backup leak, SQL injection, compromised replica) can mint valid portal tokens until the operator rotates. Mitigation is operational: backup encryption, restricted DB credentials, network isolation.
- **No rotation grace = host SaaS integration friction.** The host application must update its environment variable / secrets store and restart its token-mint workers (or accept a brief window of mint failures) the moment rotate is clicked. There is no "old + new both work for 5 minutes" cushion. Hosts that mint tokens on every page render absorb this gracefully (next render uses the new key); hosts that cache mints longer will see brief failures.

### Neutral

- **At-rest encryption is deferred, not refused.** If a future deployment posture mandates encryption-at-rest beyond filesystem-level protection (e.g. compliance regimes that require column-level encryption), `pgcrypto` or an envelope-encryption layer can be introduced without changing the wire contract. The plaintext column is the *initial* choice, not a permanent one.
- **Multi-replica eventual consistency on rotate is bounded by the cache TTL.** Same trade-off the engine already makes for `SigningSecret` — see ADR-005 for the parallel CORS-cache TTL discussion.

## Alternatives considered

- **`pgcrypto`-encrypted column.** Rejected for v0.2.0: adds a key-management problem (where does the encryption key live?) without removing the underlying compromise scenario (whoever holds the encryption key can decrypt). Defers, doesn't solve.
- **Rotation grace window (overlapping validity).** Rejected for v0.2.0: doubles the blast radius of a leaked key (now two keys are in flight), complicates the cache (two-key lookup per app), and conflicts with the "one-click revoke" UX. If host integration friction becomes a recurring complaint, this can be revisited as an opt-in.
- **External key store (Vault, KMS).** Out of scope for a self-hosted reference deployment. Operators with strict requirements can wrap the connection string in their KMS layer.

## Revisit triggers

This decision should be reopened if any of the following becomes true:

- A production user reports that "no rotation grace" causes recurring outages they can't engineer around with idempotent retries.
- A compliance regime (SOC 2 Type II, HIPAA, PCI) is requested for the self-hosted distribution and requires column-level encryption.
- The portal signing key gets used for anything other than HS256 JWT verification (e.g. signing webhooks back to the host SaaS), changing the threat model.
