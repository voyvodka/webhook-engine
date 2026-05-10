/**
 * Browser-side JWT mint for the portal-host sample.
 *
 * DEMO ONLY — do not copy this into production.
 *
 * In production, JWT minting MUST happen on your host SaaS backend — the only
 * place that is allowed to hold the per-app PORTAL_SIGNING_KEY. Putting the
 * signing key in browser code exposes it to every visitor and defeats the
 * entire security model.
 *
 * This file exists solely to make the sample self-contained (no server needed).
 * The mock-fetch.ts shim never validates the signature so the hard-coded key
 * here is purely cosmetic.
 */

const FAKE_SIGNING_KEY = "demo-only-not-for-production-use!!!!!!!!!!!!!!!!";

function base64url(obj: unknown): string {
  return btoa(JSON.stringify(obj))
    .replace(/=+$/, "")
    .replace(/\+/g, "-")
    .replace(/\//g, "_");
}

export async function mintPortalToken(
  appId: string,
  capabilities: string[],
): Promise<string> {
  const now = Math.floor(Date.now() / 1000);

  const header = { alg: "HS256", typ: "JWT" };
  const payload = {
    sub: appId,
    appId,
    cap: capabilities,
    iat: now,
    nbf: now,
    exp: now + 600, // 10 minutes — well within the engine's 15-minute cap
  };

  const signingInput = `${base64url(header)}.${base64url(payload)}`;

  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(FAKE_SIGNING_KEY),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );

  const sigBytes = new Uint8Array(
    await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(signingInput)),
  );

  // Use reduce instead of spread so the pattern stays safe to copy-paste
  // for larger payloads — spreading a long Uint8Array can blow the call stack.
  const sigB64 = btoa(sigBytes.reduce((s, b) => s + String.fromCharCode(b), ""))
    .replace(/=+$/, "")
    .replace(/\+/g, "-")
    .replace(/\//g, "_");

  return `${signingInput}.${sigB64}`;
}
