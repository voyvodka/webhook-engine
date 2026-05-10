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
  PortalAttempt,
  PortalTestResult,
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

const ATTEMPT: PortalAttempt = {
  id: "att-1",
  messageId: "msg-1",
  attemptNumber: 1,
  status: "success",
  statusCode: 200,
  error: null,
  latencyMs: 142,
  createdAt: "2026-05-10T18:30:00Z",
};

const TEST_RESULT: PortalTestResult = {
  success: true,
  statusCode: 200,
  latencyMs: 87,
  responseBody: "ok",
  error: null,
  request: {
    url: "https://consumer.example.com/hooks",
    headers: {
      "webhook-id": "msg_abc123",
      "webhook-timestamp": "1715363400",
      "webhook-signature": "v1,abc123def456",
      "Content-Type": "application/json",
    },
    body: '{"orderId":"abc123"}',
  },
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

  // ── testEndpoint ──────────────────────────────────────────────────────────

  it("testEndpoint — POSTs to correct URL and returns PortalTestResult", async () => {
    let capturedUrl = "";
    let capturedBody: unknown;
    const fetch = createMockFetch([
      {
        method: "POST",
        pattern: "/api/v1/portal/endpoints/ep-1/test",
        handler: (url, init) => {
          capturedUrl = url;
          capturedBody = JSON.parse(init?.body as string);
          return jsonOk(TEST_RESULT);
        },
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });
    const result = await client.testEndpoint("ep-1", {
      eventType: "order.created",
      payload: { orderId: "abc123" },
    });

    expect(capturedUrl).toContain("/api/v1/portal/endpoints/ep-1/test");
    expect(capturedBody).toMatchObject({
      eventType: "order.created",
      payload: { orderId: "abc123" },
    });
    expect(result.statusCode).toBe(200);
    expect(result.latencyMs).toBe(87);
    expect(result.responseBody).toBe("ok");
    expect(result.request.headers["webhook-signature"]).toMatch(/^v1,/);
  });

  it("testEndpoint — includes customHeaders in body when provided", async () => {
    let capturedBody: unknown;
    const fetch = createMockFetch([
      {
        method: "POST",
        pattern: "/test",
        handler: (_url, init) => {
          capturedBody = JSON.parse(init?.body as string);
          return jsonOk(TEST_RESULT);
        },
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });
    await client.testEndpoint("ep-1", {
      eventType: "order.created",
      payload: {},
      customHeaders: { "X-Custom": "value" },
    });

    expect(capturedBody).toMatchObject({ customHeaders: { "X-Custom": "value" } });
  });

  it("testEndpoint — 403 throws PortalError with PORTAL_INSUFFICIENT_CAPABILITY", async () => {
    const fetch = createMockFetch([
      {
        method: "POST",
        pattern: "/test",
        handler: () =>
          jsonError(403, "PORTAL_INSUFFICIENT_CAPABILITY", "Insufficient capability"),
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });

    let caught: PortalError | null = null;
    try {
      await client.testEndpoint("ep-1", { eventType: "order.created", payload: {} });
    } catch (err) {
      caught = err as PortalError;
    }

    expect(caught).toBeInstanceOf(PortalError);
    expect(caught?.code).toBe("PORTAL_INSUFFICIENT_CAPABILITY");
    expect(caught?.status).toBe(403);
  });

  it("testEndpoint — 422 populates fieldErrors", async () => {
    const fetch = createMockFetch([
      {
        method: "POST",
        pattern: "/test",
        handler: () =>
          jsonError(422, "VALIDATION_FAILED", "Validation failed", {
            eventType: "Event type is required",
          }),
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });

    let caught: PortalError | null = null;
    try {
      await client.testEndpoint("ep-1", { eventType: "", payload: {} });
    } catch (err) {
      caught = err as PortalError;
    }

    expect(caught?.fieldErrors?.["eventType"]).toBe("Event type is required");
  });

  // ── listAttempts ──────────────────────────────────────────────────────────

  it("listAttempts — GETs correct URL with pagination params", async () => {
    let capturedUrl = "";
    const fetch = createMockFetch([
      {
        method: "GET",
        pattern: "/attempts",
        handler: (url) => {
          capturedUrl = url;
          return jsonList([ATTEMPT], { page: 2, pageSize: 10, total: 47 });
        },
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });
    const result = await client.listAttempts("ep-1", { page: 2, pageSize: 10 });

    expect(capturedUrl).toContain("/api/v1/portal/endpoints/ep-1/attempts");
    expect(capturedUrl).toContain("page=2");
    expect(capturedUrl).toContain("pageSize=10");
    expect(result.data).toHaveLength(1);
    expect(result.data[0]?.id).toBe("att-1");
  });

  it("listAttempts — pagination shape is flattened into result.pagination", async () => {
    const fetch = createMockFetch([
      {
        method: "GET",
        pattern: "/attempts",
        handler: () =>
          jsonList([ATTEMPT], { page: 2, pageSize: 10, total: 47 }),
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });
    const result = await client.listAttempts("ep-1", { page: 2, pageSize: 10 });

    expect(result.pagination).toEqual({ page: 2, pageSize: 10, total: 47 });
  });

  it("listAttempts — 403 throws PortalError with PORTAL_INSUFFICIENT_CAPABILITY", async () => {
    const fetch = createMockFetch([
      {
        method: "GET",
        pattern: "/attempts",
        handler: () =>
          jsonError(403, "PORTAL_INSUFFICIENT_CAPABILITY", "Insufficient capability"),
      },
    ]);
    const client = createPortalClient({ baseUrl: BASE, token: TOKEN, fetch });

    await expect(client.listAttempts("ep-1")).rejects.toMatchObject({
      code: "PORTAL_INSUFFICIENT_CAPABILITY",
      status: 403,
    });
  });
});
