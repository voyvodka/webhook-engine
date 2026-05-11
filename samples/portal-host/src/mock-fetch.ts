/**
 * In-memory fetch shim for the portal-host sample.
 *
 * Intercepts calls to https://hooks.example.com/api/v1/portal/* and serves
 * synthetic data so the sample runs without a running WebhookEngine instance.
 *
 * The real engine is the source of truth for the API contract. This file
 * demonstrates what calling code looks like, not faithful API reproduction.
 */

import type {
  PortalAttempt,
  PortalEndpointDetail,
  PortalEndpointSummary,
  PortalEventTypeListItem,
  PortalTestResult,
} from "@webhookengine/endpoint-manager";

function uuid(): string {
  return crypto.randomUUID();
}

function now(): string {
  return new Date().toISOString();
}

const EVENT_TYPES: PortalEventTypeListItem[] = [
  { id: uuid(), name: "order.created", description: "A new order was placed" },
  { id: uuid(), name: "order.shipped", description: "An order was shipped" },
  { id: uuid(), name: "refund.issued", description: "A refund was issued to the customer" },
];

type StoredEndpoint = PortalEndpointDetail & { isActive: boolean };

const store = new Map<string, StoredEndpoint>([
  [
    "ep-0001",
    {
      id: "ep-0001",
      url: "https://api.acme.example/webhooks/orders",
      description: "Order lifecycle events",
      isActive: true,
      hasSecretOverride: false,
      filterEventTypes: ["order.created", "order.shipped"],
      customHeaders: {},
      createdAt: "2026-04-01T10:00:00.000Z",
      updatedAt: "2026-04-01T10:00:00.000Z",
    },
  ],
  [
    "ep-0002",
    {
      id: "ep-0002",
      url: "https://api.acme.example/webhooks/refunds",
      description: "Refund notifications",
      isActive: true,
      hasSecretOverride: true,
      filterEventTypes: ["refund.issued"],
      customHeaders: { "X-Source": "webhookengine" },
      createdAt: "2026-04-15T09:30:00.000Z",
      updatedAt: "2026-04-20T14:00:00.000Z",
    },
  ],
  [
    "ep-0003",
    {
      id: "ep-0003",
      url: "https://legacy.acme.example/hook",
      description: null,
      isActive: false,
      hasSecretOverride: false,
      filterEventTypes: [],
      customHeaders: {},
      createdAt: "2026-03-10T08:00:00.000Z",
      updatedAt: "2026-04-25T11:15:00.000Z",
    },
  ],
]);

function toSummary(ep: StoredEndpoint): PortalEndpointSummary {
  return {
    id: ep.id,
    url: ep.url,
    description: ep.description,
    isActive: ep.isActive,
    hasSecretOverride: ep.hasSecretOverride,
    filterEventTypes: ep.filterEventTypes,
    createdAt: ep.createdAt,
  };
}

function ok(data: unknown, extra?: Record<string, unknown>): Response {
  return new Response(JSON.stringify({ data, ...extra }), {
    status: 200,
    headers: { "content-type": "application/json" },
  });
}

function noContent(): Response {
  return new Response(null, { status: 204 });
}

function notFound(): Response {
  return new Response(
    JSON.stringify({ error: { code: "PORTAL_NOT_FOUND", message: "Not found" } }),
    { status: 404, headers: { "content-type": "application/json" } },
  );
}

const BASE = "https://hooks.example.com";

const originalFetch = globalThis.fetch;

export function installMockFetch(): void {
  globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const url = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
    if (!url.startsWith(BASE)) return originalFetch(input, init);

    const path = url.slice(BASE.length).split("?")[0];
    const method = (init?.method ?? "GET").toUpperCase();

    // GET /api/v1/portal/endpoints
    if (method === "GET" && path === "/api/v1/portal/endpoints") {
      const items = [...store.values()].map(toSummary);
      return ok(items, { meta: { pagination: { page: 1, pageSize: 20, total: items.length } } });
    }

    // GET /api/v1/portal/event-types
    if (method === "GET" && path === "/api/v1/portal/event-types") {
      return ok(EVENT_TYPES);
    }

    // POST /api/v1/portal/endpoints
    if (method === "POST" && path === "/api/v1/portal/endpoints") {
      const body = JSON.parse((init?.body as string) ?? "{}") as Partial<PortalEndpointDetail>;
      const id = uuid();
      const ep: StoredEndpoint = {
        id,
        url: body.url ?? "",
        description: body.description ?? null,
        isActive: true,
        hasSecretOverride: false,
        filterEventTypes: body.filterEventTypes ?? [],
        customHeaders: body.customHeaders ?? {},
        createdAt: now(),
        updatedAt: now(),
      };
      store.set(id, ep);
      return ok(ep);
    }

    const endpointMatch = path.match(/^\/api\/v1\/portal\/endpoints\/([^/]+)(\/.*)?$/);
    if (endpointMatch) {
      const [, id, sub] = endpointMatch as [string, string, string | undefined];
      const ep = store.get(id);

      if (!ep) return notFound();

      // GET /api/v1/portal/endpoints/{id}
      if (method === "GET" && !sub) return ok(ep);

      // PUT /api/v1/portal/endpoints/{id}
      if (method === "PUT" && !sub) {
        const body = JSON.parse((init?.body as string) ?? "{}") as Partial<PortalEndpointDetail>;
        const updated: StoredEndpoint = { ...ep, ...body, id, updatedAt: now() };
        store.set(id, updated);
        return ok(updated);
      }

      // DELETE /api/v1/portal/endpoints/{id}
      if (method === "DELETE" && !sub) {
        store.delete(id);
        return noContent();
      }

      // POST /api/v1/portal/endpoints/{id}/enable
      if (method === "POST" && sub === "/enable") {
        store.set(id, { ...ep, isActive: true, updatedAt: now() });
        return noContent();
      }

      // POST /api/v1/portal/endpoints/{id}/disable
      if (method === "POST" && sub === "/disable") {
        store.set(id, { ...ep, isActive: false, updatedAt: now() });
        return noContent();
      }

      // POST /api/v1/portal/endpoints/{id}/test
      if (method === "POST" && sub === "/test") {
        const result: PortalTestResult = {
          success: true,
          statusCode: 200,
          latencyMs: 42,
          responseBody: '{"received":true}',
          error: null,
          request: {
            url: ep.url,
            headers: {
              "webhook-id": uuid(),
              "webhook-timestamp": String(Math.floor(Date.now() / 1000)),
              "webhook-signature": "v1,dGVzdC1zaWduYXR1cmU=",
              "content-type": "application/json",
            },
            body: '{"eventType":"order.created","payload":{}}',
          },
        };
        return ok(result);
      }

      // GET /api/v1/portal/endpoints/{id}/attempts
      if (method === "GET" && sub === "/attempts") {
        const attempts: PortalAttempt[] = Array.from({ length: 5 }, (_, i) => ({
          id: uuid(),
          messageId: uuid(),
          attemptNumber: i + 1,
          status: i === 4 ? "failure" : "success",
          statusCode: i === 4 ? 500 : 200,
          error: i === 4 ? "Internal Server Error" : null,
          latencyMs: 30 + i * 10,
          createdAt: new Date(Date.now() - (5 - i) * 3_600_000).toISOString(),
        }));
        return ok(attempts, { meta: { pagination: { page: 1, pageSize: 50, total: 5 } } });
      }
    }

    return new Response(JSON.stringify({ error: { code: "NOT_FOUND", message: "Mock: route not matched" } }), {
      status: 404,
      headers: { "content-type": "application/json" },
    });
  };
}
