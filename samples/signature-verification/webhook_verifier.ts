// ---------------------------------------------------------------
// WebhookEngine — Signature Verification Helper (TypeScript)
// ---------------------------------------------------------------
// Copy this file into your project to verify webhook signatures.
// Works with Node.js (built-in crypto module) — no npm packages needed.
//
// Usage (Express):
//   import { verifyWebhook } from './webhook_verifier';
//
//   app.post('/webhook', express.raw({ type: '*/*' }), (req, res) => {
//     const isValid = verifyWebhook({
//       webhookId:        req.headers['webhook-id'] as string,
//       webhookTimestamp:  req.headers['webhook-timestamp'] as string,
//       webhookSignature:  req.headers['webhook-signature'] as string,
//       body:              req.body.toString('utf-8'),
//       secret:            process.env.WEBHOOK_SECRET!,
//     });
//
//     if (!isValid) return res.status(401).json({ error: 'Invalid signature' });
//     // Process webhook...
//     res.json({ status: 'received' });
//   });
// ---------------------------------------------------------------

import { createHmac, timingSafeEqual } from "crypto";

interface VerifyOptions {
  /** Value of the 'webhook-id' header */
  webhookId: string;
  /** Value of the 'webhook-timestamp' header (Unix seconds) */
  webhookTimestamp: string;
  /** Value of the 'webhook-signature' header (e.g. "v1,base64...") */
  webhookSignature: string;
  /** The raw request body as a string */
  body: string;
  /** The signing secret for the application/endpoint */
  secret: string;
  /** Timestamp tolerance in seconds. Defaults to 300 (5 minutes). */
  toleranceSeconds?: number;
}

/**
 * Verifies a WebhookEngine webhook signature.
 * Returns true if the signature is valid and the timestamp is within tolerance.
 */
export function verifyWebhook(options: VerifyOptions): boolean {
  const {
    webhookId,
    webhookTimestamp,
    webhookSignature,
    body,
    secret,
    toleranceSeconds = 300,
  } = options;

  if (!webhookId || !webhookTimestamp || !webhookSignature || !secret) {
    return false;
  }

  // Check timestamp tolerance
  const ts = parseInt(webhookTimestamp, 10);
  if (isNaN(ts)) return false;

  const now = Math.floor(Date.now() / 1000);
  const drift = Math.abs(now - ts);
  if (drift > toleranceSeconds) return false;

  // Compute expected signature
  const signedContent = `${webhookId}.${webhookTimestamp}.${body}`;
  const secretBytes = resolveSecretBytes(secret);
  const hash = createHmac("sha256", secretBytes)
    .update(signedContent, "utf-8")
    .digest("base64");
  const expectedSignature = `v1,${hash}`;

  // The header may contain multiple signatures separated by spaces
  const signatures = webhookSignature.split(" ");
  const expectedBuf = Buffer.from(expectedSignature, "utf-8");

  for (const sig of signatures) {
    const sigBuf = Buffer.from(sig.trim(), "utf-8");

    // Use constant-time comparison to prevent timing attacks
    if (
      sigBuf.length === expectedBuf.length &&
      timingSafeEqual(sigBuf, expectedBuf)
    ) {
      return true;
    }
  }

  return false;
}

function resolveSecretBytes(secret: string): Buffer {
  // Support whsec_ prefix (Standard Webhooks format)
  if (secret.toLowerCase().startsWith("whsec_")) {
    return Buffer.from(secret, "utf-8");
  }

  // Try base64 decoding first
  const base64Decoded = Buffer.from(secret, "base64");
  if (base64Decoded.toString("base64") === secret) {
    return base64Decoded;
  }

  return Buffer.from(secret, "utf-8");
}
