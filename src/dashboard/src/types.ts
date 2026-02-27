export type HealthState = "healthy" | "degraded" | "failed";

export type CircuitState = "closed" | "open" | "halfopen";

export type MessageStatusType =
  | "Pending"
  | "Sending"
  | "Delivered"
  | "Failed"
  | "DeadLetter";

export type AttemptStatusType = "Success" | "Failure";

// ── Dashboard Overview ──────────────────────────

export interface DashboardOverview {
  last24h: {
    totalMessages: number;
    delivered: number;
    failed: number;
    pending: number;
    deadLetter: number;
    successRate: number;
    avgLatencyMs: number;
  };
  endpoints: {
    total: number;
    healthy: number;
    degraded: number;
    failed: number;
  };
  queueDepth: number;
}

export interface TimelineBucket {
  timestamp: string;
  delivered: number;
  failed: number;
}

// ── Applications ────────────────────────────────

export interface ApplicationRow {
  id: string;
  name: string;
  apiKeyPrefix: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ApplicationCreateResult {
  id: string;
  name: string;
  apiKey: string;
  signingSecret: string;
  isActive: boolean;
  createdAt: string;
}

// ── Endpoints ───────────────────────────────────

export interface EndpointRow {
  id: string;
  appId: string;
  appName: string | null;
  url: string;
  description: string | null;
  status: string;
  circuitState: CircuitState;
  eventTypes: string[];
  eventTypeIds?: string[];
  createdAt: string;
  updatedAt: string;
}

export interface EventTypeSummary {
  id: string;
  appId: string;
  name: string;
  description: string | null;
  createdAt: string;
}

// ── Messages ────────────────────────────────────

export interface MessageRow {
  id: string;
  appId: string;
  endpointId: string;
  endpointUrl: string | null;
  eventType: string | null;
  eventTypeId: string;
  status: MessageStatusType;
  attemptCount: number;
  maxRetries: number;
  payload: string;
  eventId: string | null;
  scheduledAt: string;
  deliveredAt: string | null;
  createdAt: string;
}

export interface MessageAttemptRow {
  id: string;
  attemptNumber: number;
  status: AttemptStatusType;
  statusCode: number | null;
  requestHeaders: string | null;
  responseBody: string | null;
  error: string | null;
  latencyMs: number;
  createdAt: string;
}

export interface MessageDetail extends MessageRow {
  attempts: MessageAttemptRow[];
}

// ── Pagination ──────────────────────────────────

export interface Pagination {
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNext: boolean;
  hasPrev: boolean;
}
