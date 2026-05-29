export type PortalCapability =
  | "endpoints:read"
  | "endpoints:write"
  | "endpoints:test"
  | "attempts:read";

/**
 * Endpoint status as served by the engine — the lowercased `EndpointStatus`
 * enum. `active`/`disabled` are operator/portal-controlled; `degraded`/`failed`
 * are set automatically by the circuit breaker. Unknown future values are
 * tolerated by widening to `string` at the badge layer.
 */
export type PortalEndpointStatus = "active" | "degraded" | "failed" | "disabled";

export interface PortalAppState {
  portalEnabled: boolean;
  allowedOrigins: string[];
  rotatedAt: string | null;
}

export interface PortalEndpointSummary {
  id: string;
  url: string;
  description: string | null;
  status: PortalEndpointStatus;
  hasSecretOverride: boolean;
  filterEventTypes: string[];
  createdAt: string;
}

export interface PortalEndpointDetail {
  id: string;
  url: string;
  description: string | null;
  status: PortalEndpointStatus;
  hasSecretOverride: boolean;
  filterEventTypes: string[];
  /**
   * Header NAMES only — the engine deliberately never returns custom-header
   * values to the portal (they may carry shared secrets). On edit, an update
   * that omits `customHeaders` preserves the stored values server-side; sending
   * `customHeaders` replaces the full set (the engine cannot merge).
   */
  customHeaderNames: string[];
  createdAt: string;
  updatedAt: string;
}

export interface PortalEventTypeListItem {
  id: string;
  name: string;
  description: string | null;
}

/**
 * Single delivery attempt row returned by GET /api/v1/portal/endpoints/{id}/attempts.
 * Field names mirror PortalAttemptRow on the server (PortalDtos.cs).
 */
export interface PortalAttempt {
  id: string;
  messageId: string;
  attemptNumber: number;
  /** Lowercased AttemptStatus enum: "success" | "failed" | "timeout" | "sending" */
  status: string;
  /** HTTP status code; null when a network-level error occurred before a response */
  statusCode: number | null;
  error: string | null;
  latencyMs: number;
  createdAt: string;
}

/**
 * Result returned by POST /api/v1/portal/endpoints/{id}/test.
 * Field names mirror EndpointTestResult on the server (EndpointTestModels.cs).
 */
export interface PortalTestResult {
  success: boolean;
  statusCode: number;
  latencyMs: number;
  responseBody: string;
  error: string | null;
  request: PortalTestRequestPreview;
}

/**
 * The signed request that was actually sent to the endpoint.
 * Mirrors EndpointTestRequestPreview on the server.
 */
export interface PortalTestRequestPreview {
  url: string;
  headers: Record<string, string>;
  body: string;
}

export interface PortalCreateEndpointInput {
  url: string;
  description?: string | null;
  filterEventTypes?: string[];
  customHeaders?: Record<string, string>;
  secretOverride?: string | null;
}

export interface PortalUpdateEndpointInput {
  url: string;
  description?: string | null;
  filterEventTypes?: string[];
  customHeaders?: Record<string, string>;
  secretOverride?: string | null;
}

export interface EndpointManagerProps {
  baseUrl: string;
  token: string;
  appId: string;
  capabilities?: PortalCapability[];
  theme?: "dark" | "light" | "system";
  className?: string;
  onError?: (error: Error) => void;
  onUnauthorized?: () => void;
}
