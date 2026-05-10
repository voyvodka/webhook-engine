import type {
  PortalCapability,
  PortalCreateEndpointInput,
  PortalEndpointDetail,
  PortalEndpointSummary,
  PortalEventTypeListItem,
  PortalUpdateEndpointInput,
} from "../types.js";

// Re-export PortalCapability so consumers can use it from the client module.
export type { PortalCapability };

export interface PortalClientOptions {
  baseUrl: string;
  token: string;
  /** Injected for tests — defaults to globalThis.fetch */
  fetch?: typeof fetch;
  onError?: (error: PortalError) => void;
  onUnauthorized?: () => void;
}

export interface PortalPagination {
  page: number;
  pageSize: number;
  total: number;
}

export interface PortalListResult<T> {
  data: T[];
  pagination: PortalPagination;
}

export class PortalError extends Error {
  readonly code: string;
  readonly status: number;
  readonly fieldErrors?: Record<string, string>;

  constructor(
    message: string,
    code: string,
    status: number,
    fieldErrors?: Record<string, string>,
  ) {
    super(message);
    this.name = "PortalError";
    this.code = code;
    this.status = status;
    this.fieldErrors = fieldErrors;
  }
}

export interface PortalClient {
  listEndpoints(params?: {
    page?: number;
    pageSize?: number;
  }): Promise<PortalListResult<PortalEndpointSummary>>;
  getEndpoint(id: string): Promise<PortalEndpointDetail>;
  createEndpoint(input: PortalCreateEndpointInput): Promise<PortalEndpointDetail>;
  updateEndpoint(id: string, input: PortalUpdateEndpointInput): Promise<PortalEndpointDetail>;
  deleteEndpoint(id: string): Promise<void>;
  enableEndpoint(id: string): Promise<void>;
  disableEndpoint(id: string): Promise<void>;
  listEventTypes(): Promise<PortalEventTypeListItem[]>;
  // Step 9 will add testEndpoint + listAttempts.
  testEndpoint(id: string, payload: Record<string, unknown>): Promise<never>;
  listAttempts(id: string, params?: { page?: number; pageSize?: number }): Promise<never>;
}

interface ApiErrorEnvelope {
  error: {
    code: string;
    message: string;
    fieldErrors?: Record<string, string>;
  };
  meta: {
    requestId: string;
  };
}

interface ApiDataEnvelope<T> {
  data: T;
  meta?: {
    pagination?: PortalPagination;
    requestId?: string;
  };
}

export function createPortalClient(options: PortalClientOptions): PortalClient {
  const {
    baseUrl,
    token,
    fetch: fetchImpl = globalThis.fetch,
    onError,
    onUnauthorized,
  } = options;

  const base = baseUrl.endsWith("/") ? baseUrl.slice(0, -1) : baseUrl;

  function buildUrl(path: string, query?: Record<string, string>): string {
    const url = new URL(`${base}${path}`);
    if (query) {
      const params = new URLSearchParams(query);
      params.forEach((value, key) => url.searchParams.set(key, value));
    }
    return url.toString();
  }

  async function request<T>(
    method: string,
    path: string,
    opts?: {
      body?: unknown;
      query?: Record<string, string>;
    },
  ): Promise<T> {
    const url = buildUrl(path, opts?.query);
    const headers: Record<string, string> = {
      Authorization: `Bearer ${token}`,
    };
    if (opts?.body !== undefined) {
      headers["Content-Type"] = "application/json";
    }

    const response = await fetchImpl(url, {
      method,
      headers,
      body: opts?.body !== undefined ? JSON.stringify(opts.body) : undefined,
    });

    if (response.status === 204) {
      return undefined as T;
    }

    let json: unknown;
    const contentType = response.headers.get("content-type") ?? "";
    if (contentType.includes("application/json")) {
      json = await response.json();
    }

    if (!response.ok) {
      const envelope = json as ApiErrorEnvelope | undefined;
      const code = envelope?.error?.code ?? "UNKNOWN_ERROR";
      const message = envelope?.error?.message ?? `HTTP ${response.status}`;
      const fieldErrors = envelope?.error?.fieldErrors;
      const err = new PortalError(message, code, response.status, fieldErrors);

      if (response.status === 401) {
        onUnauthorized?.();
      }
      onError?.(err);
      throw err;
    }

    const envelope = json as ApiDataEnvelope<T>;
    return envelope.data;
  }

  async function requestList<T>(
    path: string,
    params?: { page?: number; pageSize?: number },
  ): Promise<PortalListResult<T>> {
    const query: Record<string, string> = {};
    if (params?.page !== undefined) query["page"] = String(params.page);
    if (params?.pageSize !== undefined) query["pageSize"] = String(params.pageSize);

    const url = buildUrl(path, Object.keys(query).length > 0 ? query : undefined);
    const headers: Record<string, string> = {
      Authorization: `Bearer ${token}`,
    };

    const response = await fetchImpl(url, { method: "GET", headers });

    let json: unknown;
    const contentType = response.headers.get("content-type") ?? "";
    if (contentType.includes("application/json")) {
      json = await response.json();
    }

    if (!response.ok) {
      const envelope = json as ApiErrorEnvelope | undefined;
      const code = envelope?.error?.code ?? "UNKNOWN_ERROR";
      const message = envelope?.error?.message ?? `HTTP ${response.status}`;
      const fieldErrors = envelope?.error?.fieldErrors;
      const err = new PortalError(message, code, response.status, fieldErrors);
      if (response.status === 401) {
        onUnauthorized?.();
      }
      onError?.(err);
      throw err;
    }

    const envelope = json as ApiDataEnvelope<T[]>;
    const pagination: PortalPagination = envelope.meta?.pagination ?? {
      page: params?.page ?? 1,
      pageSize: params?.pageSize ?? 20,
      total: (envelope.data as T[]).length,
    };
    return { data: envelope.data, pagination };
  }

  return {
    listEndpoints(params) {
      return requestList<PortalEndpointSummary>("/api/v1/portal/endpoints", params);
    },

    getEndpoint(id) {
      return request<PortalEndpointDetail>("GET", `/api/v1/portal/endpoints/${id}`);
    },

    createEndpoint(input) {
      // Deliberately exclude transformExpression, transformEnabled, allowedIpsJson.
      const body: PortalCreateEndpointInput = {
        url: input.url,
        description: input.description,
        filterEventTypes: input.filterEventTypes,
        customHeaders: input.customHeaders,
        secretOverride: input.secretOverride,
      };
      return request<PortalEndpointDetail>("POST", "/api/v1/portal/endpoints", { body });
    },

    updateEndpoint(id, input) {
      const body: PortalUpdateEndpointInput = {
        url: input.url,
        description: input.description,
        filterEventTypes: input.filterEventTypes,
        customHeaders: input.customHeaders,
        secretOverride: input.secretOverride,
      };
      return request<PortalEndpointDetail>("PUT", `/api/v1/portal/endpoints/${id}`, { body });
    },

    deleteEndpoint(id) {
      return request<void>("DELETE", `/api/v1/portal/endpoints/${id}`);
    },

    enableEndpoint(id) {
      return request<void>("POST", `/api/v1/portal/endpoints/${id}/enable`);
    },

    disableEndpoint(id) {
      return request<void>("POST", `/api/v1/portal/endpoints/${id}/disable`);
    },

    listEventTypes() {
      return request<PortalEventTypeListItem[]>("GET", "/api/v1/portal/event-types");
    },

    testEndpoint(_id, _payload) {
      throw new Error("Not implemented yet — lands in B1 Step 9");
    },

    listAttempts(_id, _params) {
      throw new Error("Not implemented yet — lands in B1 Step 9");
    },
  };
}
