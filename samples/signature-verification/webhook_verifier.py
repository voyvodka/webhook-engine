"""
WebhookEngine — Signature Verification Helper (Python)

Copy this file into your project to verify webhook signatures.
No external dependencies required — uses only the Python standard library.

Usage (Flask):

    from webhook_verifier import verify_webhook

    @app.post("/webhook")
    def handle_webhook():
        is_valid = verify_webhook(
            webhook_id=request.headers.get("webhook-id", ""),
            webhook_timestamp=request.headers.get("webhook-timestamp", ""),
            webhook_signature=request.headers.get("webhook-signature", ""),
            body=request.get_data(as_text=True),
            secret=os.environ["WEBHOOK_SECRET"],
        )

        if not is_valid:
            return {"error": "Invalid signature"}, 401
        # Process webhook...
        return {"status": "received"}

Usage (FastAPI):

    from webhook_verifier import verify_webhook
    from fastapi import Request, HTTPException

    @app.post("/webhook")
    async def handle_webhook(request: Request):
        body = (await request.body()).decode("utf-8")
        is_valid = verify_webhook(
            webhook_id=request.headers.get("webhook-id", ""),
            webhook_timestamp=request.headers.get("webhook-timestamp", ""),
            webhook_signature=request.headers.get("webhook-signature", ""),
            body=body,
            secret=os.environ["WEBHOOK_SECRET"],
        )

        if not is_valid:
            raise HTTPException(status_code=401, detail="Invalid signature")
        return {"status": "received"}
"""

import base64
import hashlib
import hmac
import time


def verify_webhook(
    webhook_id: str,
    webhook_timestamp: str,
    webhook_signature: str,
    body: str,
    secret: str,
    tolerance_seconds: int = 300,
) -> bool:
    """
    Verify a WebhookEngine webhook signature.

    Args:
        webhook_id:          Value of the 'webhook-id' header.
        webhook_timestamp:   Value of the 'webhook-timestamp' header (Unix seconds).
        webhook_signature:   Value of the 'webhook-signature' header (e.g. "v1,base64...").
        body:                The raw request body as a string.
        secret:              The signing secret for the application/endpoint.
        tolerance_seconds:   Timestamp tolerance in seconds. Defaults to 300 (5 minutes).

    Returns:
        True if the signature is valid and the timestamp is within tolerance.
    """
    if not all([webhook_id, webhook_timestamp, webhook_signature, secret]):
        return False

    # Check timestamp tolerance
    try:
        ts = int(webhook_timestamp)
    except (ValueError, TypeError):
        return False

    now = int(time.time())
    drift = abs(now - ts)
    if drift > tolerance_seconds:
        return False

    # Compute expected signature
    signed_content = f"{webhook_id}.{webhook_timestamp}.{body}"
    secret_bytes = _resolve_secret_bytes(secret)
    digest = hmac.new(secret_bytes, signed_content.encode("utf-8"), hashlib.sha256).digest()
    expected_signature = f"v1,{base64.b64encode(digest).decode('utf-8')}"

    # The header may contain multiple signatures separated by spaces
    signatures = webhook_signature.split(" ")
    for sig in signatures:
        # Use constant-time comparison to prevent timing attacks
        if hmac.compare_digest(sig.strip(), expected_signature):
            return True

    return False


def _resolve_secret_bytes(secret: str) -> bytes:
    """
    Resolve the signing secret to bytes.
    Supports whsec_ prefix (Standard Webhooks format), base64-encoded, and raw strings.
    """
    # Support whsec_ prefix
    if secret.lower().startswith("whsec_"):
        return secret.encode("utf-8")

    # Try base64 decoding first
    try:
        decoded = base64.b64decode(secret, validate=True)
        # Verify it's actually valid base64 (round-trip check)
        if base64.b64encode(decoded).decode("utf-8") == secret:
            return decoded
    except Exception:
        pass

    return secret.encode("utf-8")
