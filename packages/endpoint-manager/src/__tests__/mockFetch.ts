export type MockHandler = (
  url: string,
  init: RequestInit | undefined,
) => Promise<Response> | Response;

export interface MockRoute {
  method: string;
  pattern: string | RegExp;
  handler: MockHandler;
}

/**
 * Creates a mock `fetch` implementation for testing the portal client.
 *
 * Routes are matched in order; the first matching route wins.
 * A route matches when both the HTTP method and the URL pattern match.
 */
export function createMockFetch(routes: MockRoute[]): typeof fetch {
  return async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const url = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
    const method = (init?.method ?? "GET").toUpperCase();

    for (const route of routes) {
      const methodMatch = route.method.toUpperCase() === method;
      const urlMatch =
        typeof route.pattern === "string"
          ? url.includes(route.pattern)
          : route.pattern.test(url);

      if (methodMatch && urlMatch) {
        return route.handler(url, init);
      }
    }

    return new Response(
      JSON.stringify({ error: { code: "NOT_FOUND", message: "No matching mock route" }, meta: { requestId: "test" } }),
      { status: 404, headers: { "content-type": "application/json" } },
    );
  };
}

/** Convenience: build a 200 JSON response wrapping data in { data: ..., meta: ... } */
export function jsonOk<T>(data: T, meta?: Record<string, unknown>): Response {
  return new Response(JSON.stringify({ data, meta: meta ?? {} }), {
    status: 200,
    headers: { "content-type": "application/json" },
  });
}

/** Convenience: build a paginated 200 JSON response */
export function jsonList<T>(
  data: T[],
  pagination: { page: number; pageSize: number; total: number },
): Response {
  return new Response(
    JSON.stringify({ data, meta: { pagination } }),
    { status: 200, headers: { "content-type": "application/json" } },
  );
}

/** Convenience: 204 No Content */
export function noContent(): Response {
  return new Response(null, { status: 204 });
}

/** Convenience: error response */
export function jsonError(
  status: number,
  code: string,
  message: string,
  fieldErrors?: Record<string, string>,
): Response {
  return new Response(
    JSON.stringify({
      error: { code, message, fieldErrors },
      meta: { requestId: "test" },
    }),
    { status, headers: { "content-type": "application/json" } },
  );
}
