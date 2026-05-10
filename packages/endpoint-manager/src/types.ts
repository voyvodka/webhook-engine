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

export interface PortalAttempt {
  id: string;
  endpointId: string;
  status: string;
  httpStatus: number | null;
  latencyMs: number | null;
  occurredAt: string;
  responseBody: string | null;
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
