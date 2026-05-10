import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { EndpointManager } from "../src/index.js";
import type {
  PortalEndpointSummary,
  PortalEndpointDetail,
  PortalEventTypeListItem,
  PortalAttempt,
  PortalTestResult,
} from "../src/types.js";
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

// Synthetic attempt history — mix of success/failure, various latencies.
const ATTEMPTS_STORE: Record<string, PortalAttempt[]> = {
  "ep-1": [
    {
      id: "att-1",
      messageId: "msg-001",
      attemptNumber: 1,
      status: "success",
      statusCode: 200,
      error: null,
      latencyMs: 124,
      createdAt: new Date(Date.now() - 1000 * 60 * 5).toISOString(),
    },
    {
      id: "att-2",
      messageId: "msg-002",
      attemptNumber: 1,
      status: "failure",
      statusCode: 503,
      error: "Service temporarily unavailable",
      latencyMs: 3001,
      createdAt: new Date(Date.now() - 1000 * 60 * 30).toISOString(),
    },
    {
      id: "att-3",
      messageId: "msg-002",
      attemptNumber: 2,
      status: "success",
      statusCode: 200,
      error: null,
      latencyMs: 89,
      createdAt: new Date(Date.now() - 1000 * 60 * 25).toISOString(),
    },
    {
      id: "att-4",
      messageId: "msg-003",
      attemptNumber: 1,
      status: "failure",
      statusCode: null,
      error: "connect ECONNREFUSED 203.0.113.42:443",
      latencyMs: 5000,
      createdAt: new Date(Date.now() - 1000 * 60 * 60 * 2).toISOString(),
    },
    {
      id: "att-5",
      messageId: "msg-004",
      attemptNumber: 1,
      status: "success",
      statusCode: 201,
      error: null,
      latencyMs: 201,
      createdAt: new Date(Date.now() - 1000 * 60 * 60 * 5).toISOString(),
    },
    {
      id: "att-6",
      messageId: "msg-005",
      attemptNumber: 1,
      status: "failure",
      statusCode: 404,
      error: "Not Found",
      latencyMs: 55,
      createdAt: new Date(Date.now() - 1000 * 60 * 60 * 8).toISOString(),
    },
    {
      id: "att-7",
      messageId: "msg-006",
      attemptNumber: 1,
      status: "success",
      statusCode: 200,
      error: null,
      latencyMs: 310,
      createdAt: new Date(Date.now() - 1000 * 60 * 60 * 24).toISOString(),
    },
  ],
};

function getAttempts(endpointId: string): PortalAttempt[] {
  return ATTEMPTS_STORE[endpointId] ?? [];
}

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

  // POST /api/v1/portal/endpoints/:id/test
  const testMatch = url.match(/\/endpoints\/([^/]+)\/test$/);
  if (method === "POST" && testMatch) {
    const id = testMatch[1]!;
    const ep = store.find((e) => e.id === id);
    if (!ep) {
      return Promise.resolve(
        new Response(
          JSON.stringify({ error: { code: "PORTAL_NOT_FOUND", message: "Not found" }, meta: {} }),
          { status: 404, headers: { "content-type": "application/json" } },
        ),
      );
    }
    const result: PortalTestResult = {
      success: true,
      statusCode: 200,
      latencyMs: 87,
      responseBody: "ok",
      error: null,
      request: {
        url: ep.url,
        headers: {
          "webhook-id": `msg_${Math.random().toString(36).slice(2, 10)}`,
          "webhook-timestamp": String(Math.floor(Date.now() / 1000)),
          "webhook-signature": `v1,${btoa("demo-signature-placeholder")}`,
          "Content-Type": "application/json",
        },
        body: init?.body as string ?? "{}",
      },
    };
    return ok(result);
  }

  // GET /api/v1/portal/endpoints/:id/attempts
  const attemptsMatch = url.match(/\/endpoints\/([^/]+)\/attempts/);
  if (method === "GET" && attemptsMatch) {
    const id = attemptsMatch[1]!;
    const params = new URL(url).searchParams;
    const page = parseInt(params.get("page") ?? "1", 10);
    const pageSize = parseInt(params.get("pageSize") ?? "20", 10);
    const all = getAttempts(id);
    const sliced = all.slice((page - 1) * pageSize, page * pageSize);
    return Promise.resolve(
      new Response(
        JSON.stringify({
          data: sliced,
          meta: { pagination: { page, pageSize, total: all.length } },
        }),
        { status: 200, headers: { "content-type": "application/json" } },
      ),
    );
  }

  // POST /api/v1/portal/endpoints/:id/enable
  const enableMatch = url.match(/\/endpoints\/([^/]+)\/enable$/);
  if (method === "POST" && enableMatch) {
    const id = enableMatch[1]!;
    const ep = store.find((e) => e.id === id);
    if (ep) ep.isActive = true;
    return noContent();
  }

  // POST /api/v1/portal/endpoints/:id/disable
  const disableMatch = url.match(/\/endpoints\/([^/]+)\/disable$/);
  if (method === "POST" && disableMatch) {
    const id = disableMatch[1]!;
    const ep = store.find((e) => e.id === id);
    if (ep) ep.isActive = false;
    return noContent();
  }

  // GET /api/v1/portal/endpoints/:id (single)
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

// Type alias just to keep the cast clean.
type ListResult<T> = PortalListResult<T>;
void (null as unknown as ListResult<unknown>);

// ---------------------------------------------------------------------------
// App
// ---------------------------------------------------------------------------
function App() {
  return (
    <EndpointManager
      baseUrl="http://localhost:0000"
      token="demo-token"
      appId="00000000-0000-0000-0000-000000000000"
      capabilities={["endpoints:read", "endpoints:write", "endpoints:test", "attempts:read"]}
      // Inject the mock fetch so no real HTTP is made.
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
