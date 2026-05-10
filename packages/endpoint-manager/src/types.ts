export type PortalCapability =
  | "endpoints:read"
  | "endpoints:write"
  | "endpoints:test"
  | "attempts:read";

export interface PortalAppState {
  portalEnabled: boolean;
  allowedOrigins: string[];
  rotatedAt: string | null;
}

export interface PortalEndpointSummary {
  id: string;
  url: string;
  description: string | null;
  isActive: boolean;
  hasSecretOverride: boolean;
  filterEventTypes: string[];
  createdAt: string;
}

export interface PortalEndpointDetail {
  id: string;
  url: string;
  description: string | null;
  isActive: boolean;
  hasSecretOverride: boolean;
  filterEventTypes: string[];
  customHeaders: Record<string, string>;
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
  /** "success" | "failure" — serialised from AttemptStatus enum as camelCase */
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
