import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { EndpointManager } from "../src/index.js";
import type { PortalEndpointSummary, PortalEndpointDetail, PortalEventTypeListItem } from "../src/types.js";
import type { PortalListResult } from "../src/api/createPortalClient.js";

// ---------------------------------------------------------------------------
// In-memory mock data store for the demo.
// ---------------------------------------------------------------------------
let nextId = 4;
const store: PortalEndpointDetail[] = [
  {
    id: "ep-1",
    url: "https://acme-corp.example.com/webhooks/orders",
    description: "Order events",
    isActive: true,
    hasSecretOverride: false,
    filterEventTypes: ["et-1"],
    customHeaders: { "X-Source": "webhookengine" },
    createdAt: new Date(Date.now() - 1000 * 60 * 60 * 24 * 7).toISOString(),
    updatedAt: new Date(Date.now() - 1000 * 60 * 60 * 24 * 2).toISOString(),
  },
  {
    id: "ep-2",
    url: "https://acme-corp.example.com/webhooks/payments",
    description: "Payment notifications",
    isActive: false,
    hasSecretOverride: true,
    filterEventTypes: [],
    customHeaders: {},
    createdAt: new Date(Date.now() - 1000 * 60 * 60 * 24 * 3).toISOString(),
    updatedAt: new Date(Date.now() - 1000 * 60 * 60).toISOString(),
  },
  {
    id: "ep-3",
    url: "https://acme-corp.example.com/webhooks/all",
    description: null,
    isActive: true,
    hasSecretOverride: false,
    filterEventTypes: [],
    customHeaders: {},
    createdAt: new Date(Date.now() - 1000 * 60 * 30).toISOString(),
    updatedAt: new Date(Date.now() - 1000 * 60 * 30).toISOString(),
  },
];

const EVENT_TYPES: PortalEventTypeListItem[] = [
  { id: "et-1", name: "order.created", description: "Fired when an order is placed" },
  { id: "et-2", name: "order.fulfilled", description: "Fired when an order ships" },
  { id: "et-3", name: "payment.succeeded", description: null },
  { id: "et-4", name: "payment.failed", description: null },
];

function toSummary(d: PortalEndpointDetail): PortalEndpointSummary {
  return {
    id: d.id,
    url: d.url,
    description: d.description,
    isActive: d.isActive,
    hasSecretOverride: d.hasSecretOverride,
    filterEventTypes: d.filterEventTypes,
    createdAt: d.createdAt,
  };
}

// ---------------------------------------------------------------------------
// Mock fetch that mirrors the portal API contract.
// ---------------------------------------------------------------------------
function mockFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  const url = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
  const method = (init?.method ?? "GET").toUpperCase();

  const ok = <T>(data: T, status = 200) =>
    Promise.resolve(
      new Response(JSON.stringify({ data, meta: {} }), {
        status,
        headers: { "content-type": "application/json" },
      }),
    );

  const noContent = () => Promise.resolve(new Response(null, { status: 204 }));

  // GET /api/v1/portal/event-types
  if (method === "GET" && url.includes("/event-types")) {
    return ok(EVENT_TYPES);
  }

  // GET /api/v1/portal/endpoints/:id/enable or /disable
  const enableMatch = url.match(/\/endpoints\/([^/]+)\/enable$/);
  if (method === "POST" && enableMatch) {
    const id = enableMatch[1]!;
    const ep = store.find((e) => e.id === id);
    if (ep) ep.isActive = true;
    return noContent();
  }

  const disableMatch = url.match(/\/endpoints\/([^/]+)\/disable$/);
  if (method === "POST" && disableMatch) {
    const id = disableMatch[1]!;
    const ep = store.find((e) => e.id === id);
    if (ep) ep.isActive = false;
    return noContent();
  }

  // GET /api/v1/portal/endpoints/:id
  const singleMatch = url.match(/\/endpoints\/([^/?]+)$/);
  if (method === "GET" && singleMatch) {
    const ep = store.find((e) => e.id === singleMatch[1]);
    if (!ep)
      return Promise.resolve(
        new Response(
          JSON.stringify({ error: { code: "PORTAL_NOT_FOUND", message: "Not found" }, meta: {} }),
          { status: 404, headers: { "content-type": "application/json" } },
        ),
      );
    return ok(ep);
  }

  // PUT /api/v1/portal/endpoints/:id
  if (method === "PUT" && singleMatch) {
    const id = singleMatch[1]!;
    const idx = store.findIndex((e) => e.id === id);
    if (idx === -1)
      return Promise.resolve(new Response(null, { status: 404 }));
    const body = JSON.parse(init?.body as string ?? "{}") as Partial<PortalEndpointDetail>;
    store[idx] = { ...store[idx]!, ...body, updatedAt: new Date().toISOString() };
    return ok(store[idx]!);
  }

  // DELETE /api/v1/portal/endpoints/:id
  if (method === "DELETE" && singleMatch) {
    const id = singleMatch[1]!;
    const idx = store.findIndex((e) => e.id === id);
    if (idx !== -1) store.splice(idx, 1);
    return noContent();
  }

  // GET /api/v1/portal/endpoints (list)
  if (method === "GET" && url.includes("/endpoints")) {
    const params = new URL(url).searchParams;
    const page = parseInt(params.get("page") ?? "1", 10);
    const pageSize = parseInt(params.get("pageSize") ?? "20", 10);
    const summaries = store.map(toSummary);
    const sliced = summaries.slice((page - 1) * pageSize, page * pageSize);
    return Promise.resolve(
      new Response(
        JSON.stringify({
          data: sliced,
          meta: { pagination: { page, pageSize, total: summaries.length } },
        }),
        { status: 200, headers: { "content-type": "application/json" } },
      ),
    );
  }

  // POST /api/v1/portal/endpoints (create)
  if (method === "POST" && url.includes("/endpoints")) {
    const body = JSON.parse(init?.body as string ?? "{}") as Partial<PortalEndpointDetail>;
    const newEp: PortalEndpointDetail = {
      id: `ep-${nextId++}`,
      url: body.url ?? "",
      description: body.description ?? null,
      isActive: true,
      hasSecretOverride: !!body.hasSecretOverride,
      filterEventTypes: body.filterEventTypes ?? [],
      customHeaders: body.customHeaders ?? {},
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };
    store.push(newEp);
    return ok(newEp, 201);
  }

  return Promise.resolve(new Response(null, { status: 404 }));
}

// ---------------------------------------------------------------------------
// App
// ---------------------------------------------------------------------------
function App() {
  return (
    <EndpointManager
      baseUrl="http://localhost:0000"
      token="demo-token"
      appId="00000000-0000-0000-0000-000000000000"
      capabilities={["endpoints:read", "endpoints:write"]}
      // Inject the mock fetch so no real HTTP is made.
      // EndpointManager exposes fetch injection via createPortalClient internally —
      // for the demo we monkey-patch globalThis.fetch before the component mounts.
    />
  );
}

// Patch global fetch for the demo so all requests hit the in-memory store.
(globalThis as typeof globalThis & { fetch: typeof fetch }).fetch = mockFetch as typeof fetch;

const root = document.getElementById("root");
if (!root) throw new Error("Missing #root");

createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
