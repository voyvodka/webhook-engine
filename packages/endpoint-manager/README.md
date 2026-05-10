# @webhookengine/endpoint-manager

Embeddable React portal for managing webhook endpoints — pairs with a self-hosted WebhookEngine instance.

## Installation

```bash
npm install @webhookengine/endpoint-manager
```

> **Note:** This package is not yet published to npm. Publication happens in B1 Step 11. Until then, workspace consumers can reference it directly via the Bun workspace:
>
> ```json
> { "dependencies": { "@webhookengine/endpoint-manager": "workspace:*" } }
> ```

## Peer dependencies

Requires React 19 or later:

```bash
npm install react@^19 react-dom@^19
```

## Usage

```tsx
import { EndpointManager } from "@webhookengine/endpoint-manager";

<EndpointManager
  baseUrl="https://hooks.example.com"
  token="<portal-jwt>"
  appId="<your-app-id>"
  theme="dark"
/>
```

The full component suite (endpoint list, editor, tester, attempt history) ships in **B1 Step 8**. The current version renders a placeholder that confirms the import path is correctly wired — no setup changes will be required when the real UI lands.

## Integration model

Portal JWTs are minted by your WebhookEngine instance (`POST /api/v1/portal/token`). The token encodes the app ID and a `capabilities` claim that controls which UI panels are visible. CORS and allowed-origins configuration lives in the WebhookEngine admin dashboard.

Full integration documentation: [`docs/PORTAL.md`](https://github.com/voyvodka/webhook-engine/blob/main/docs/PORTAL.md)

## Links

- GitHub: [voyvodka/webhook-engine](https://github.com/voyvodka/webhook-engine)
- Landing page: [webhook.sametozkan.com.tr](https://webhook.sametozkan.com.tr)
- License: MIT
