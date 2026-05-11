# portal-host sample

A minimal Vite + React app that shows what a SaaS host's integration with
`@webhookengine/endpoint-manager` looks like from the outside.

The sample is self-contained: it mocks the fetch layer so you do not need a
running WebhookEngine instance to explore the component.

---

## What this shows

- How to embed `<EndpointManager />` in a host SaaS settings page.
- How to pass `baseUrl`, `token`, `appId`, and `capabilities` props.
- How to override the component's CSS custom properties at the page level to
  match your brand (see `src/styles.css` and the `:root` block in `index.html`).
- Where portal-token minting belongs (server side, not browser side — see the
  caveat below).

---

## Run it locally

```bash
cd samples/portal-host
bun install
bun run dev
```

Opens on <http://localhost:5173>. The mock fetch intercepts all calls to
`https://hooks.example.com/api/v1/portal/*` and serves an in-memory state
seeded with three example endpoints.

---

## mint-token.ts caveat — production warning

`src/mint-token.ts` mints a JWT in the browser using a hard-coded key. This is
acceptable in a local sample because the mock-fetch shim never validates the
signature and the key is public in the repo.

**In production this pattern is forbidden.** The signing key would be visible to
every visitor who opens DevTools. JWT minting MUST live on your host SaaS
backend — the only place that is allowed to hold the per-app
`PORTAL_SIGNING_KEY`. Your frontend should call your own
`POST /internal/portal-token` endpoint and receive back only the short-lived JWT.

See [docs/PORTAL.md — Section 4](../../docs/PORTAL.md#4-host-sas-integration) for
the full server-side integration model, including a `jose`-based Node.js example.

---

## Further reading

- [docs/PORTAL.md](../../docs/PORTAL.md) — full integration model, security
  model, configuration reference, and limits.
- [packages/endpoint-manager/README.md](../../packages/endpoint-manager/README.md)
  — component API, props, CSS custom properties.
