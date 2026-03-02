import { useEffect, useMemo, useState } from "react";
import { getOverview, getTimeline } from "../api/dashboardApi";
import { DeliveryTimeline } from "../components/DeliveryTimeline";
import { useDeliveryFeed } from "../hooks/useDeliveryFeed";
import type { DashboardOverview, TimelineBucket } from "../types";
import {
  Activity,
  CheckCircle2,
  XCircle,
  Clock,
  Gauge,
  Timer,
  Wifi,
  WifiOff
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
  const { events, connected } = useDeliveryFeed(20);

  useEffect(() => {
    let isMounted = true;

    async function load() {
      setIsLoading(true);
      try {
        const [overviewData, timelineData] = await Promise.all([getOverview(), getTimeline()]);
        if (!isMounted) return;
        setOverview(overviewData);
        setTimeline(timelineData);
      } catch {
        if (!isMounted) return;
        setOverview(fallbackOverview);
        setTimeline(fallbackTimeline);
      } finally {
        if (isMounted) setIsLoading(false);
      }
    }

    void load();
    return () => { isMounted = false; };
  }, []);

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
                      <StatusBadge status={event.status} />
                    </td>
                    <td className="py-1.5 text-text-secondary">{event.attemptCount}</td>
                    <td className="py-1.5 font-mono text-xs text-text-secondary">
                      {event.latencyMs ? `${event.latencyMs}ms` : "--"}
                    </td>
                    <td className="py-1.5 text-xs text-text-muted">{new Date(event.timestamp).toLocaleTimeString()}</td>
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

function StatusBadge({ status }: { status: string }) {
  const styles: Record<string, string> = {
    Delivered: "text-success bg-success-soft",
    Failed: "text-danger bg-danger-soft",
    DeadLetter: "text-danger bg-danger-soft",
    Pending: "text-warning bg-warning-soft",
    Sending: "text-accent bg-accent-soft"
  };

  return (
    <span className={`inline-block text-xs font-medium px-1.5 py-0.5 rounded ${styles[status] ?? "text-text-muted bg-surface-2"}`}>
      {status === "DeadLetter" ? "Dead Letter" : status}
    </span>
  );
}
