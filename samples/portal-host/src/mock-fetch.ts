/**
 * In-memory fetch shim for the portal-host sample.
 *
 * Intercepts calls to https://hooks.example.com/api/v1/portal/* and serves
 * synthetic data so the sample runs without a running WebhookEngine instance.
 *
 * The response shapes mirror the real engine (Contracts/Portal/PortalDtos.cs):
 * endpoints expose `status` + `customHeaderNames` (names only — values are kept
 * internally here and never returned), `filterEventTypes` are event-type IDs,
 * and updates use PATCH. This is a demonstration of calling code, kept faithful
 * to the contract so it can't drift from the package client.
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

/**
 * Internal store record. Holds the custom-header VALUES (which the engine keeps
 * private) so the test/disable flows are realistic; only the names are exposed.
 */
interface StoredEndpoint {
  id: string;
  url: string;
  description: string | null;
  status: PortalEndpointSummary["status"];
  hasSecretOverride: boolean;
  filterEventTypes: string[];
  customHeaders: Record<string, string>;
  createdAt: string;
  updatedAt: string;
}

const store = new Map<string, StoredEndpoint>([
  [
    "ep-0001",
    {
      id: "ep-0001",
      url: "https://api.acme.example/webhooks/orders",
      description: "Order lifecycle events",
      status: "active",
      hasSecretOverride: false,
      filterEventTypes: [EVENT_TYPES[0]!.id, EVENT_TYPES[1]!.id],
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
      status: "active",
      hasSecretOverride: true,
      filterEventTypes: [EVENT_TYPES[2]!.id],
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
      status: "disabled",
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
    status: ep.status,
    hasSecretOverride: ep.hasSecretOverride,
    filterEventTypes: ep.filterEventTypes,
    createdAt: ep.createdAt,
  };
}

function toDetail(ep: StoredEndpoint): PortalEndpointDetail {
  return {
    id: ep.id,
    url: ep.url,
    description: ep.description,
    status: ep.status,
    hasSecretOverride: ep.hasSecretOverride,
    filterEventTypes: ep.filterEventTypes,
    customHeaderNames: Object.keys(ep.customHeaders),
    createdAt: ep.createdAt,
    updatedAt: ep.updatedAt,
  };
}

interface UpdateBody {
  url?: string;
  description?: string | null;
  filterEventTypes?: string[];
  customHeaders?: Record<string, string>;
  secretOverride?: string | null;
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
const BASE_URL = new URL(BASE);

const originalFetch = globalThis.fetch;

export function installMockFetch(): void {
  globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const url = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
    // Parse the URL and compare the host explicitly. A naive
    // url.startsWith(BASE) check would also match
    // https://hooks.example.com.attacker.com/... — fine for a sample
    // but the kind of pattern that gets copy-pasted into production code.
    let parsed: URL;
    try {
      parsed = new URL(url);
    } catch {
      return originalFetch(input, init);
    }
    if (parsed.protocol !== BASE_URL.protocol || parsed.host !== BASE_URL.host) {
      return originalFetch(input, init);
    }

    const path = parsed.pathname;
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
      const body = JSON.parse((init?.body as string) ?? "{}") as UpdateBody;
      const id = uuid();
      const ep: StoredEndpoint = {
        id,
        url: body.url ?? "",
        description: body.description ?? null,
        status: "active",
        hasSecretOverride: !!body.secretOverride,
        filterEventTypes: body.filterEventTypes ?? [],
        customHeaders: body.customHeaders ?? {},
        createdAt: now(),
        updatedAt: now(),
      };
      store.set(id, ep);
      return ok(toDetail(ep));
    }

    const endpointMatch = path.match(/^\/api\/v1\/portal\/endpoints\/([^/]+)(\/.*)?$/);
    if (endpointMatch) {
      const [, id, sub] = endpointMatch as [string, string, string | undefined];
      const ep = store.get(id);

      if (!ep) return notFound();

      // GET /api/v1/portal/endpoints/{id}
      if (method === "GET" && !sub) return ok(toDetail(ep));

      // PATCH /api/v1/portal/endpoints/{id} — partial merge, matching the engine.
      if (method === "PATCH" && !sub) {
        const body = JSON.parse((init?.body as string) ?? "{}") as UpdateBody;
        const updated: StoredEndpoint = {
          ...ep,
          ...(body.url !== undefined ? { url: body.url } : {}),
          ...(body.description !== undefined ? { description: body.description } : {}),
          ...(body.filterEventTypes !== undefined ? { filterEventTypes: body.filterEventTypes } : {}),
          // Only replace headers when the client actually sends them — mirrors
          // the engine, which preserves stored headers when the field is absent.
          ...(body.customHeaders !== undefined ? { customHeaders: body.customHeaders } : {}),
          // Mirror the engine: a present-but-empty/whitespace secretOverride
          // clears the override; a non-empty value sets it; null/absent leaves it.
          ...(body.secretOverride != null
            ? { hasSecretOverride: body.secretOverride.trim().length > 0 }
            : {}),
          updatedAt: now(),
        };
        store.set(id, updated);
        return ok(toDetail(updated));
      }

      // DELETE /api/v1/portal/endpoints/{id}
      if (method === "DELETE" && !sub) {
        store.delete(id);
        return noContent();
      }

      // POST /api/v1/portal/endpoints/{id}/enable
      if (method === "POST" && sub === "/enable") {
        store.set(id, { ...ep, status: "active", updatedAt: now() });
        return noContent();
      }

      // POST /api/v1/portal/endpoints/{id}/disable
      if (method === "POST" && sub === "/disable") {
        store.set(id, { ...ep, status: "disabled", updatedAt: now() });
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
          status: i === 4 ? "failed" : "success",
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
