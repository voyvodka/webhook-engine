import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  getDevTrafficStatus,
  getOverview,
  getTimeline,
  seedDevTraffic,
  startDevTraffic,
  stopDevTraffic,
  type DevTrafficSeedResult,
  type DevTrafficStatus
} from "../api/dashboardApi";
import { DeliveryTimeline } from "../components/DeliveryTimeline";
import { StatusBadge } from "../components/StatusBadge";
import { useDeliveryFeed } from "../hooks/useDeliveryFeed";
import type { DashboardOverview, TimelineBucket } from "../types";
import { formatLocaleTime } from "../utils/dateTime";
import {
  Activity,
  CheckCircle2,
  XCircle,
  Clock,
  Gauge,
  Timer,
  Wifi,
  WifiOff,
  Play,
  Square,
  FlaskConical
} from "lucide-react";

const fallbackOverview: DashboardOverview = {
  last24h: {
    totalMessages: 0,
    delivered: 0,
    failed: 0,
    pending: 0,
    deadLetter: 0,
    successRate: 0,
    avgLatencyMs: 0
  },
  endpoints: {
    total: 0,
    healthy: 0,
    degraded: 0,
    failed: 0,
    disabled: 0
  },
  queueDepth: 0
};

const fallbackTimeline: TimelineBucket[] = [];

interface StatCard {
  title: string;
  value: string | number;
  icon: React.ElementType;
  accent: "accent" | "success" | "danger" | "warning";
}

const accentMap = {
  accent: {
    icon: "text-accent",
    bg: "bg-accent-soft",
    border: "border-accent/20"
  },
  success: {
    icon: "text-success",
    bg: "bg-success-soft",
    border: "border-success/20"
  },
  danger: {
    icon: "text-danger",
    bg: "bg-danger-soft",
    border: "border-danger/20"
  },
  warning: {
    icon: "text-warning",
    bg: "bg-warning-soft",
    border: "border-warning/20"
  }
};

export function DashboardPage() {
  const [overview, setOverview] = useState<DashboardOverview>(fallbackOverview);
  const [timeline, setTimeline] = useState<TimelineBucket[]>(fallbackTimeline);
  const [isLoading, setIsLoading] = useState(true);
  const [devToolsAvailable, setDevToolsAvailable] = useState(false);
  const [devStatus, setDevStatus] = useState<DevTrafficStatus | null>(null);
  const [devSeedResult, setDevSeedResult] = useState<DevTrafficSeedResult | null>(null);
  const [devActionLoading, setDevActionLoading] = useState(false);
  const { events, connected } = useDeliveryFeed(20);

  const isMountedRef = useRef(true);
  const realtimeSyncPendingRef = useRef(false);
  const realtimeSyncInFlightRef = useRef(false);
  const hasEverConnectedRef = useRef(false);
  // Floor on how often the SignalR-driven refresh actually fires the
  // overview/timeline GETs. The hub emits a delivery event for every single
  // message the worker processes; without a min-interval guard a busy queue
  // (e.g. dev-traffic seed) burns one fetch per second indefinitely. Three
  // seconds is the rough cadence a human can perceive, so the dashboard
  // stays "live" without pummelling the API.
  const lastRealtimeRefreshAtRef = useRef(0);
  const REALTIME_MIN_INTERVAL_MS = 3000;

  const refreshDashboardData = useCallback(async (showLoading = false) => {
    if (showLoading) setIsLoading(true);

    try {
      const [overviewData, timelineData] = await Promise.all([getOverview(), getTimeline()]);
      if (!isMountedRef.current) return;

      setOverview(overviewData);
      setTimeline(timelineData);
    } catch {
      if (!isMountedRef.current) return;

      if (showLoading) {
        setOverview(fallbackOverview);
        setTimeline(fallbackTimeline);
      }
    } finally {
      if (showLoading && isMountedRef.current) {
        setIsLoading(false);
      }
    }
  }, []);

  const loadDevStatus = useCallback(async () => {
    try {
      const status = await getDevTrafficStatus();
      setDevToolsAvailable(true);
      setDevStatus(status);
    } catch {
      setDevToolsAvailable(false);
      setDevStatus(null);
    }
  }, []);

  useEffect(() => {
    Promise.resolve()
      .then(() => refreshDashboardData(true))
      .catch(() => { /* surfaced via fallback state */ });

    return () => {
      isMountedRef.current = false;
    };
  }, [refreshDashboardData]);

  useEffect(() => {
    Promise.resolve()
      .then(() => loadDevStatus())
      .catch(() => { /* dev tools optional, errors swallowed */ });
  }, [loadDevStatus]);

  useEffect(() => {
    if (!devToolsAvailable) return;

    // Adaptive cadence: while a dev-traffic flow is actually running the
    // status block is interesting enough to poll quickly (3 s), but once the
    // generator is idle the same poll is just network noise — drop it to 30 s
    // so a long-running idle dashboard doesn't churn the dev-status endpoint.
    const isRunning = devStatus?.running === true;
    const interval = isRunning ? 3000 : 30_000;

    const timer = window.setInterval(() => {
      void loadDevStatus();
    }, interval);

    return () => window.clearInterval(timer);
  }, [devToolsAvailable, loadDevStatus, devStatus?.running]);

  useEffect(() => {
    const latestEvent = events[0];
    if (!latestEvent) return;

    realtimeSyncPendingRef.current = true;
  }, [events]);

  useEffect(() => {
    if (!connected) return;

    if (!hasEverConnectedRef.current) {
      hasEverConnectedRef.current = true;
      return;
    }

    realtimeSyncPendingRef.current = true;
  }, [connected]);

  useEffect(() => {
    // Tick at 5 s instead of 1 s and guard with a 3 s min-interval since the
    // last completed refresh, so a flood of SignalR delivery events (e.g.
    // dev-traffic seed) coalesces into at most one overview/timeline fetch
    // every few seconds rather than one per second. This is a tactical fix
    // ahead of F12; once TanStack Query is wired in, the queryClient's own
    // staleTime + invalidateQueries replaces this manual debounce entirely.
    const timer = window.setInterval(() => {
      if (!realtimeSyncPendingRef.current || realtimeSyncInFlightRef.current) {
        return;
      }

      const now = Date.now();
      if (now - lastRealtimeRefreshAtRef.current < REALTIME_MIN_INTERVAL_MS) {
        return;
      }

      realtimeSyncPendingRef.current = false;
      realtimeSyncInFlightRef.current = true;

      void refreshDashboardData(false).finally(() => {
        realtimeSyncInFlightRef.current = false;
        lastRealtimeRefreshAtRef.current = Date.now();
      });
    }, 5000);

    return () => window.clearInterval(timer);
  }, [refreshDashboardData]);

  const handleSeedOnce = async () => {
    setDevActionLoading(true);
    try {
      const result = await seedDevTraffic({ messages: 1 });
      setDevSeedResult(result);
      await Promise.all([loadDevStatus(), refreshDashboardData(false)]);
    } finally {
      setDevActionLoading(false);
    }
  };

  const handleStartFlow = async () => {
    setDevActionLoading(true);
    try {
      const status = await startDevTraffic({ intervalMs: 1200, messagesPerTick: 6 });
      setDevStatus(status);
      setDevSeedResult(null);
    } finally {
      setDevActionLoading(false);
    }
  };

  const handleStopFlow = async () => {
    setDevActionLoading(true);
    try {
      const status = await stopDevTraffic();
      setDevStatus(status);
    } finally {
      setDevActionLoading(false);
    }
  };

  const cards = useMemo<StatCard[]>(
    () => [
      { title: "Total", value: overview.last24h.totalMessages, icon: Activity, accent: "accent" },
      { title: "Delivered", value: overview.last24h.delivered, icon: CheckCircle2, accent: "success" },
      { title: "Failed", value: overview.last24h.failed, icon: XCircle, accent: "danger" },
      { title: "Queue", value: overview.queueDepth, icon: Clock, accent: "warning" },
      { title: "Success", value: `${overview.last24h.successRate}%`, icon: Gauge, accent: "success" },
      { title: "Latency", value: `${overview.last24h.avgLatencyMs}ms`, icon: Timer, accent: "accent" }
    ],
    [overview]
  );

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="animate-fade-in-up">
        <h1 className="text-lg font-semibold">Overview</h1>
        <p className="text-sm text-text-muted mt-0.5">Last 24h delivery stats, throughput, and live feed.</p>
      </div>

      {devToolsAvailable && (
        <div className="rounded-xl border border-accent/20 bg-accent-soft/50 p-3 animate-fade-in-up">
          <div className="flex items-center justify-between gap-2">
            <div>
              <h2 className="text-sm font-semibold flex items-center gap-1.5">
                <FlaskConical className="w-3.5 h-3.5 text-accent" />
                Dev Traffic
              </h2>
              <p className="text-xs text-text-muted mt-0.5">
                Local demo traffic generator. When running, live feed gets continuous events.
              </p>
            </div>
            <span className={`text-xs px-2 py-1 rounded ${devStatus?.running ? "text-success bg-success-soft" : "text-text-muted bg-surface-2"}`}>
              {devStatus?.running ? "Running" : "Idle"}
            </span>
          </div>

          <div className="mt-2 flex items-center flex-wrap gap-2">
            <button
              onClick={handleSeedOnce}
              disabled={devActionLoading}
              className="text-xs font-medium px-3 py-1.5 rounded-lg border border-border bg-surface-1 text-text-secondary hover:text-text-primary hover:bg-surface-2 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              Seed Once
            </button>
            <button
              onClick={handleStartFlow}
              disabled={devActionLoading || devStatus?.running}
              className="inline-flex items-center gap-1 text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              <Play className="w-3 h-3" />
              Start Live Flow
            </button>
            <button
              onClick={handleStopFlow}
              disabled={devActionLoading || !devStatus?.running}
              className="inline-flex items-center gap-1 text-xs font-medium px-3 py-1.5 rounded-lg bg-surface-2 text-text-secondary hover:text-text-primary disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              <Square className="w-3 h-3" />
              Stop
            </button>

            <span className="text-xs text-text-muted ml-auto">
              {devStatus?.lastSeedAtUtc
                ? `Last seed: ${formatLocaleTime(devStatus.lastSeedAtUtc, { withSeconds: true })} (${devStatus.lastEnqueuedCount} msg)`
                : "No seed yet"}
            </span>
          </div>

          {devSeedResult && (
            <p className="text-xs text-text-secondary mt-2">
              {devSeedResult.enqueuedMessages > 0
                ? `Seeded ${devSeedResult.enqueuedMessages} messages across ${devSeedResult.targetedEndpoints} endpoints in ${devSeedResult.activeApplications} app(s).`
                : devSeedResult.error
                  ? devSeedResult.error
                  : `No messages sent — ${devSeedResult.activeApplications === 0 ? "no active applications found. Create an application with endpoints first." : "no active endpoints available. Endpoints may be disabled or rate-limited."}`}
            </p>
          )}

          {devStatus?.lastError && (
            <p className="text-xs text-danger mt-2">{devStatus.lastError}</p>
          )}
        </div>
      )}

      {/* Stats grid */}
      <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-2.5">
        {cards.map((card, i) => {
          const a = accentMap[card.accent];
          return (
            <div
              key={card.title}
              className={`rounded-lg border ${a.border} bg-surface-1 p-3 animate-fade-in-up`}
              style={{ animationDelay: `${i * 50}ms` }}
            >
              <div className="flex items-center gap-2 mb-2">
                <div className={`w-6 h-6 rounded-md ${a.bg} flex items-center justify-center`}>
                  <card.icon className={`w-3.5 h-3.5 ${a.icon}`} />
                </div>
                <span className="text-xs text-text-muted">{card.title}</span>
              </div>
              <span className="text-xl font-semibold font-mono">
                {isLoading ? "..." : card.value}
              </span>
            </div>
          );
        })}
      </div>

      {/* Timeline */}
      <DeliveryTimeline buckets={timeline} />

      {/* Live Feed */}
      <div className="rounded-xl border border-border bg-surface-1 p-4 animate-fade-in-up">
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-sm font-semibold">Live Feed</h2>
          <span className="flex items-center gap-1.5 text-xs">
            {connected ? (
              <>
                <Wifi className="w-3 h-3 text-success" />
                <span className="text-success">Connected</span>
              </>
            ) : (
              <>
                <WifiOff className="w-3 h-3 text-text-muted" />
                <span className="text-text-muted">Disconnected</span>
              </>
            )}
          </span>
        </div>

        {events.length === 0 ? (
          <p className="text-sm text-text-muted py-6 text-center">
            Waiting for delivery events...
          </p>
        ) : (
          <div className="overflow-auto max-h-72 -mx-4 px-4">
            <table className="w-full min-w-[560px]">
              <thead>
                <tr className="text-xs text-text-muted">
                  <th className="text-left pb-2 font-medium">Message ID</th>
                  <th className="text-left pb-2 font-medium">Status</th>
                  <th className="text-left pb-2 font-medium">Attempt</th>
                  <th className="text-left pb-2 font-medium">Latency</th>
                  <th className="text-left pb-2 font-medium">Time</th>
                </tr>
              </thead>
              <tbody className="text-sm">
                {events.map((event, i) => (
                  <tr key={`${event.messageId}-${i}`} className="border-t border-border-subtle">
                    <td className="py-1.5 font-mono text-xs text-text-secondary">{event.messageId.slice(0, 12)}</td>
                    <td className="py-1.5">
                      <StatusBadge kind={event.status} />
                    </td>
                    <td className="py-1.5 text-text-secondary">{event.attemptCount}</td>
                    <td className="py-1.5 font-mono text-xs text-text-secondary">
                      {event.latencyMs ? `${event.latencyMs}ms` : "--"}
                    </td>
                    <td className="py-1.5 text-xs text-text-muted">{formatLocaleTime(event.timestamp, { withSeconds: true })}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
