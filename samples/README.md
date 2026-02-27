# Samples

This directory contains end-to-end examples for WebhookEngine.

## Included Samples

- `WebhookEngine.Sample.Sender/` — console app that uses the .NET SDK to:
  - create an event type
  - create an endpoint
  - send a webhook
  - query delivery status and attempts
- `WebhookEngine.Sample.Receiver/` — minimal ASP.NET Core API that:
  - receives webhook `POST /webhook`
  - verifies HMAC signature headers
  - logs payload and verification result
- `signature-verification/` — copy-paste signature verification helpers for C#, TypeScript, and Python

## Run End-to-End Locally

1. Start WebhookEngine API:

```bash
dotnet run --project src/WebhookEngine.API
```

2. Create an application from the dashboard (`http://localhost:5100` in Docker mode or `http://localhost:5128` with `dotnet run`) and copy its API key.

3. Start the receiver:

```bash
dotnet run --project samples/WebhookEngine.Sample.Receiver
```

Optional: enable signature verification by setting the app signing secret:

```bash
WEBHOOK_SECRET="your-signing-secret" dotnet run --project samples/WebhookEngine.Sample.Receiver
```

4. Run the sender:

```bash
dotnet run --project samples/WebhookEngine.Sample.Sender -- "whe_abc_your-api-key"
```

Optional environment variables for sender:

- `WEBHOOKENGINE_BASE_URL` (default: `http://localhost:5100`)
- `WEBHOOKENGINE_RECEIVER_URL` (default: `http://localhost:5200/webhook`)

You should see webhook payload logs in the receiver console and status updates in the sender output.
