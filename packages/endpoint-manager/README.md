# @webhookengine/endpoint-manager

Embeddable React portal for managing webhook endpoints — pairs with a self-hosted WebhookEngine instance.

## Install

```bash
npm install @webhookengine/endpoint-manager
# or
bun add @webhookengine/endpoint-manager
# or
pnpm add @webhookengine/endpoint-manager
```

Peer dependencies (you provide these): `react ^19`, `react-dom ^19`.

## Quick start

```tsx
import { EndpointManager } from "@webhookengine/endpoint-manager";
import "@webhookengine/endpoint-manager/style.css";

export function WebhookSettingsPage() {
  // Mint this token on YOUR backend — never in the browser.
  // The signing key (Application.PortalSigningKey from the engine dashboard)
  // must stay server-side.
  const token = "<JWT minted by your backend>";

  return (
    <EndpointManager
      baseUrl="https://hooks.your-domain.com"
      token={token}
      appId="<the application id from the engine dashboard>"
      capabilities={["endpoints:read", "endpoints:write", "endpoints:test", "attempts:read"]}
    />
  );
}
```

See [`docs/PORTAL.md`](https://github.com/voyvodka/webhook-engine/blob/main/docs/PORTAL.md) for the full integration model (JWT minting on the host backend, CORS, capability scoping, security model). See [`samples/portal-host/`](https://github.com/voyvodka/webhook-engine/tree/main/samples/portal-host) for a working consumer reference app.

## Theming

The component uses CSS custom properties scoped under `.whe-portal`. Override them at `:root` or on a wrapper element to match your brand:

```css
:root {
  --color-whe-background: #ffffff;
  --color-whe-text: #111827;
  --color-whe-border: #e5e7eb;
  --color-whe-accent: #6366f1;
  --color-whe-success: #16a34a;
  --color-whe-danger: #dc2626;
  --color-whe-warning: #d97706;
}
```

## Peer dependencies

Requires React 19 or later:

```bash
npm install react@^19 react-dom@^19
```

## Engine compatibility

Requires WebhookEngine v0.2.0 or later. Earlier engine versions do not expose the `/api/v1/portal/*` route group this package consumes.

## Links

- Integration guide: [`docs/PORTAL.md`](https://github.com/voyvodka/webhook-engine/blob/main/docs/PORTAL.md)
- Reference sample: [`samples/portal-host/`](https://github.com/voyvodka/webhook-engine/tree/main/samples/portal-host)
- GitHub: [voyvodka/webhook-engine](https://github.com/voyvodka/webhook-engine)
- Landing page: [webhook.sametozkan.com.tr](https://webhook.sametozkan.com.tr)
- License: MIT
