import { describe, it, expect, vi } from "vitest";
import { createPortalClient, PortalError } from "../api/createPortalClient.js";
import {
  createMockFetch,
  jsonOk,
  jsonList,
  noContent,
  jsonError,
} from "./mockFetch.js";
import type {
  PortalEndpointSummary,
  PortalEndpointDetail,
  PortalEventTypeListItem,
} from "../types.js";

const BASE = "https://engine.example.com";
const TOKEN = "test-token";

const SUMMARY: PortalEndpointSummary = {
  id: "ep-1",
  url: "https://consumer.example.com/hooks",
  description: "Test endpoint",
  isActive: true,
  hasSecretOverride: false,
  filterEventTypes: [],
  createdAt: "2026-01-01T00:00:00Z",
};

const DETAIL: PortalEndpointDetail = {
  ...SUMMARY,
  customHeaders: {},
  updatedAt: "2026-01-01T00:00:00Z",
};

const EVENT_TYPE: PortalEventTypeListItem = {
  id: "et-1",
  name: "order.created",
  description: null,
};

describe("createPortalClient", () => {
  it("listEndpoints — returns data and pagination", async () => {
    const fetch = createMockFetch([
      {
        method: "GET",
        pattern: "/api/v1/portal/endpoints",
        handler: () =>
          jsonList([SUMMARY], { page: 1, pageSize: 20, total: 1 }),
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });
    const result = await client.listEndpoints({ page: 1, pageSize: 20 });

    expect(result.data).toHaveLength(1);
    expect(result.data[0]?.id).toBe("ep-1");
    expect(result.pagination).toEqual({ page: 1, pageSize: 20, total: 1 });
  });

  it("getEndpoint — returns unwrapped detail", async () => {
    const fetch = createMockFetch([
      {
        method: "GET",
        pattern: "/api/v1/portal/endpoints/ep-1",
        handler: () => jsonOk(DETAIL),
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });
    const result = await client.getEndpoint("ep-1");

    expect(result.id).toBe("ep-1");
    expect(result.customHeaders).toEqual({});
  });

  it("createEndpoint — sends correct body and returns detail", async () => {
    let capturedBody: unknown;
    const fetch = createMockFetch([
      {
        method: "POST",
        pattern: "/api/v1/portal/endpoints",
        handler: (_url, init) => {
          capturedBody = JSON.parse(init?.body as string);
          return jsonOk(DETAIL);
        },
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });
    const result = await client.createEndpoint({
      url: "https://consumer.example.com/hooks",
      description: "Test",
      filterEventTypes: [],
      customHeaders: {},
    });

    expect(result.id).toBe("ep-1");
    expect(capturedBody).toMatchObject({ url: "https://consumer.example.com/hooks" });
    // Must not include admin-only fields.
    expect(capturedBody).not.toHaveProperty("transformExpression");
    expect(capturedBody).not.toHaveProperty("transformEnabled");
    expect(capturedBody).not.toHaveProperty("allowedIpsJson");
  });

  it("updateEndpoint — sends correct body", async () => {
    let capturedBody: unknown;
    const fetch = createMockFetch([
      {
        method: "PUT",
        pattern: "/api/v1/portal/endpoints/ep-1",
        handler: (_url, init) => {
          capturedBody = JSON.parse(init?.body as string);
          return jsonOk({ ...DETAIL, url: "https://updated.example.com/hooks" });
        },
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });
    const result = await client.updateEndpoint("ep-1", {
      url: "https://updated.example.com/hooks",
    });

    expect(result.url).toBe("https://updated.example.com/hooks");
    expect(capturedBody).not.toHaveProperty("transformExpression");
  });

  it("deleteEndpoint — returns void on 204", async () => {
    const fetch = createMockFetch([
      {
        method: "DELETE",
        pattern: "/api/v1/portal/endpoints/ep-1",
        handler: () => noContent(),
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });
    const result = await client.deleteEndpoint("ep-1");

    expect(result).toBeUndefined();
  });

  it("enableEndpoint + disableEndpoint — both return void on 204", async () => {
    const fetch = createMockFetch([
      { method: "POST", pattern: "/enable", handler: () => noContent() },
      { method: "POST", pattern: "/disable", handler: () => noContent() },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });

    await expect(client.enableEndpoint("ep-1")).resolves.toBeUndefined();
    await expect(client.disableEndpoint("ep-1")).resolves.toBeUndefined();
  });

  it("401 triggers onUnauthorized callback and throws PortalError", async () => {
    const onUnauthorized = vi.fn();
    const fetch = createMockFetch([
      {
        method: "GET",
        pattern: "/api/v1/portal/endpoints",
        handler: () =>
          jsonError(401, "PORTAL_AUTH_REQUIRED", "Token is required"),
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch, onUnauthorized });

    await expect(client.listEndpoints()).rejects.toBeInstanceOf(PortalError);
    expect(onUnauthorized).toHaveBeenCalledOnce();
  });

  it("422 surfaces fieldErrors on PortalError", async () => {
    const fetch = createMockFetch([
      {
        method: "POST",
        pattern: "/api/v1/portal/endpoints",
        handler: () =>
          jsonError(422, "VALIDATION_FAILED", "Validation failed", {
            url: "URL must use HTTPS",
          }),
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });

    let caught: PortalError | null = null;
    try {
      await client.createEndpoint({ url: "http://not-https.example.com" });
    } catch (err) {
      caught = err as PortalError;
    }

    expect(caught).toBeInstanceOf(PortalError);
    expect(caught?.code).toBe("VALIDATION_FAILED");
    expect(caught?.status).toBe(422);
    expect(caught?.fieldErrors?.["url"]).toBe("URL must use HTTPS");
  });

  it("listEventTypes — returns array directly", async () => {
    const fetch = createMockFetch([
      {
        method: "GET",
        pattern: "/api/v1/portal/event-types",
        handler: () => jsonOk([EVENT_TYPE]),
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });
    const result = await client.listEventTypes();

    expect(result).toHaveLength(1);
    expect(result[0]?.name).toBe("order.created");
  });

  it("testEndpoint — throws 'Not implemented'", () => {
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN });
    expect(() => client.testEndpoint("ep-1", {})).toThrow("Not implemented yet");
  });

  it("listAttempts — throws 'Not implemented'", () => {
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN });
    expect(() => client.listAttempts("ep-1")).toThrow("Not implemented yet");
  });
});
