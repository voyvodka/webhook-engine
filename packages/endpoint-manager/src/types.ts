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

export interface EndpointManagerProps {
  baseUrl: string;
  token: string;
  appId: string;
  theme?: "dark" | "light" | "system";
  className?: string;
  onError?: (error: Error) => void;
}
