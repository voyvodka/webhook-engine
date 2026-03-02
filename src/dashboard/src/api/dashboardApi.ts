import type {
  DashboardOverview,
  TimelineBucket,
  ApplicationRow,
  ApplicationCreateResult,
  EndpointRow,
  EventTypeSummary,
  MessageRow,
  MessageDetail,
  Pagination
} from "../types";

// ── Helpers ─────────────────────────────────────

interface ApiEnvelope<T> {
  data: T;
  meta?: { requestId?: string; pagination?: Pagination };
}

async function parseError(response: Response): Promise<string> {
  try {
    const payload = (await response.json()) as { error?: { message?: string } };
    if (payload.error?.message) return payload.error.message;
  } catch {
    // no-op
  }
  return `Request failed with status ${response.status}`;
}

async function fetchJson<T>(url: string): Promise<T> {
  const response = await fetch(url, {
    credentials: "include",
    headers: { "Content-Type": "application/json" }
  });

  if (!response.ok) {
    throw new Error(await parseError(response));
  }

  return (await response.json()) as T;
}

async function mutate<T>(url: string, method: string, body?: unknown): Promise<T> {
  const response = await fetch(url, {
    method,
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    ...(body !== undefined ? { body: JSON.stringify(body) } : {})
  });

  if (!response.ok) {
    throw new Error(await parseError(response));
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

// ── Dashboard Overview ──────────────────────────

export async function getOverview(): Promise<DashboardOverview> {
  const payload = await fetchJson<ApiEnvelope<DashboardOverview>>("/api/v1/dashboard/overview");
  return payload.data;
}

export async function getTimeline(period = "24h", interval = "1h"): Promise<TimelineBucket[]> {
  const payload = await fetchJson<ApiEnvelope<{ buckets: TimelineBucket[] }>>(
    `/api/v1/dashboard/timeline?period=${period}&interval=${interval}`
  );
  return payload.data.buckets;
}

// ── Applications ────────────────────────────────

export async function listApplications(
  page = 1,
  pageSize = 20
): Promise<{ data: ApplicationRow[]; pagination: Pagination }> {
  const payload = await fetchJson<ApiEnvelope<ApplicationRow[]>>(
    `/api/v1/applications?page=${page}&pageSize=${pageSize}`
  );
  return { data: payload.data, pagination: payload.meta?.pagination ?? defaultPagination(page, pageSize) };
}

export async function createApplication(name: string): Promise<ApplicationCreateResult> {
  const payload = await mutate<ApiEnvelope<ApplicationCreateResult>>(
    "/api/v1/applications",
    "POST",
    { name }
  );
  return payload.data;
}

export async function updateApplication(
  id: string,
  updates: { name?: string; isActive?: boolean }
): Promise<ApplicationRow> {
  const payload = await mutate<ApiEnvelope<ApplicationRow>>(
    `/api/v1/applications/${id}`,
    "PUT",
    updates
  );
  return payload.data;
}

export async function deleteApplication(id: string): Promise<void> {
  await mutate<void>(`/api/v1/applications/${id}`, "DELETE");
}

export async function rotateApiKey(id: string): Promise<{ apiKey: string }> {
  const payload = await mutate<ApiEnvelope<{ id: string; name: string; apiKey: string }>>(
    `/api/v1/applications/${id}/rotate-key`,
    "POST"
  );
  return { apiKey: payload.data.apiKey };
}

export async function rotateSigningSecret(id: string): Promise<{ signingSecret: string }> {
  const payload = await mutate<ApiEnvelope<{ id: string; name: string; signingSecret: string }>>(
    `/api/v1/applications/${id}/rotate-secret`,
    "POST"
  );
  return { signingSecret: payload.data.signingSecret };
}

// ── Endpoints ───────────────────────────────────

export interface EndpointListParams {
  appId?: string;
  status?: string;
  page?: number;
  pageSize?: number;
}

export interface DashboardCreateEndpointRequest {
  appId: string;
  url: string;
  description?: string;
  filterEventTypes?: string[];
  customHeaders?: Record<string, string>;
  metadata?: Record<string, string>;
  secretOverride?: string;
}

export interface DashboardUpdateEndpointRequest {
  url?: string;
  description?: string;
  filterEventTypes?: string[];
  customHeaders?: Record<string, string>;
  metadata?: Record<string, string>;
  secretOverride?: string;
}

export async function listEndpoints(
  params: EndpointListParams = {}
): Promise<{ data: EndpointRow[]; pagination: Pagination }> {
  const qs = new URLSearchParams();
  if (params.appId) qs.set("appId", params.appId);
  if (params.status) qs.set("status", params.status);
  qs.set("page", String(params.page ?? 1));
  qs.set("pageSize", String(params.pageSize ?? 20));

  const payload = await fetchJson<ApiEnvelope<EndpointRow[]>>(
    `/api/v1/dashboard/endpoints?${qs.toString()}`
  );
  return {
    data: payload.data,
    pagination: payload.meta?.pagination ?? defaultPagination(params.page ?? 1, params.pageSize ?? 20)
  };
}

export async function createDashboardEndpoint(request: DashboardCreateEndpointRequest): Promise<EndpointRow> {
  const payload = await mutate<ApiEnvelope<EndpointRow>>(
    "/api/v1/dashboard/endpoints",
    "POST",
    request
  );
  return payload.data;
}

export async function updateDashboardEndpoint(endpointId: string, request: DashboardUpdateEndpointRequest): Promise<EndpointRow> {
  const payload = await mutate<ApiEnvelope<EndpointRow>>(
    `/api/v1/dashboard/endpoints/${endpointId}`,
    "PUT",
    request
  );
  return payload.data;
}

export async function listDashboardEventTypes(appId: string, includeArchived = false): Promise<EventTypeSummary[]> {
  const payload = await fetchJson<ApiEnvelope<EventTypeSummary[]>>(
    `/api/v1/dashboard/event-types?appId=${encodeURIComponent(appId)}&includeArchived=${includeArchived}`
  );
  return payload.data;
}

export interface DashboardCreateEventTypeRequest {
  appId: string;
  name: string;
  description?: string;
}

export interface DashboardUpdateEventTypeRequest {
  name?: string;
  description?: string;
}

export async function createDashboardEventType(request: DashboardCreateEventTypeRequest): Promise<EventTypeSummary> {
  const payload = await mutate<ApiEnvelope<EventTypeSummary>>(
    "/api/v1/dashboard/event-types",
    "POST",
    request
  );
  return payload.data;
}

export async function updateDashboardEventType(eventTypeId: string, request: DashboardUpdateEventTypeRequest): Promise<EventTypeSummary> {
  const payload = await mutate<ApiEnvelope<EventTypeSummary>>(
    `/api/v1/dashboard/event-types/${eventTypeId}`,
    "PUT",
    request
  );
  return payload.data;
}

export async function archiveDashboardEventType(eventTypeId: string): Promise<void> {
  await mutate<void>(`/api/v1/dashboard/event-types/${eventTypeId}`, "DELETE");
}

export async function setDashboardEndpointStatus(endpointId: string, enabled: boolean): Promise<{ id: string; status: string }> {
  const action = enabled ? "enable" : "disable";
  const payload = await mutate<ApiEnvelope<{ id: string; status: string }>>(
    `/api/v1/dashboard/endpoints/${endpointId}/${action}`,
    "POST"
  );
  return payload.data;
}

export async function deleteDashboardEndpoint(endpointId: string): Promise<void> {
  await mutate<void>(`/api/v1/dashboard/endpoints/${endpointId}`, "DELETE");
}

// ── Messages ────────────────────────────────────

export interface MessageListParams {
  appId?: string;
  status?: string;
  endpointId?: string;
  eventType?: string;
  after?: string;
  before?: string;
  page?: number;
  pageSize?: number;
}

export async function listMessages(
  params: MessageListParams = {}
): Promise<{ data: MessageRow[]; pagination: Pagination }> {
  const qs = new URLSearchParams();
  if (params.appId) qs.set("appId", params.appId);
  if (params.status) qs.set("status", params.status);
  if (params.endpointId) qs.set("endpointId", params.endpointId);
  if (params.eventType) qs.set("eventType", params.eventType);
  if (params.after) qs.set("after", params.after);
  if (params.before) qs.set("before", params.before);
  qs.set("page", String(params.page ?? 1));
  qs.set("pageSize", String(params.pageSize ?? 20));

  const payload = await fetchJson<ApiEnvelope<MessageRow[]>>(
    `/api/v1/dashboard/messages?${qs.toString()}`
  );
  return {
    data: payload.data,
    pagination: payload.meta?.pagination ?? defaultPagination(params.page ?? 1, params.pageSize ?? 20)
  };
}

export async function getMessage(messageId: string): Promise<MessageDetail> {
  const payload = await fetchJson<ApiEnvelope<MessageDetail>>(
    `/api/v1/dashboard/messages/${messageId}`
  );
  return payload.data;
}

export async function retryMessage(messageId: string): Promise<void> {
  await mutate<unknown>(`/api/v1/dashboard/messages/${messageId}/retry`, "POST");
}

export interface DashboardSendMessageRequest {
  appId: string;
  eventType: string;
  eventTypeId?: string;
  payload: unknown;
  eventId?: string;
  idempotencyKey?: string;
}

export interface DashboardSendMessageResult {
  messageIds: string[];
  endpointCount: number;
  eventType: string;
}

export async function sendDashboardMessage(request: DashboardSendMessageRequest): Promise<DashboardSendMessageResult> {
  const payload = await mutate<ApiEnvelope<DashboardSendMessageResult>>(
    "/api/v1/dashboard/messages/send",
    "POST",
    request
  );
  return payload.data;
}

// ── Util ────────────────────────────────────────

function defaultPagination(page: number, pageSize: number): Pagination {
  return { page, pageSize, totalCount: 0, totalPages: 0, hasNext: false, hasPrev: false };
}
