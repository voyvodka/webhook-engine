import { useEffect, useState } from "react";
import { Link, useParams } from "react-router";
import { PayloadViewer } from "../components/PayloadViewer";
import { RetryButton } from "../components/RetryButton";
import { getMessage } from "../api/dashboardApi";
import type { MessageDetail } from "../types";
import { formatLocaleDateTime } from "../utils/dateTime";
import {
  ArrowLeft,
  Clock,
  Globe,
  Tag,
  Hash,
  CheckCircle2,
  XCircle
} from "lucide-react";

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

export function DeliveryLogPage() {
  const { messageId } = useParams<{ messageId?: string }>();
  const [message, setMessage] = useState<MessageDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!messageId) {
      setLoading(false);
      setError("No message ID provided. Navigate here from the Messages page.");
      return;
    }

    let cancelled = false;
    async function load() {
      setLoading(true);
      setError("");
      try {
        const data = await getMessage(messageId!);
        if (!cancelled) setMessage(data);
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : "Failed to load message");
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    load();
    return () => { cancelled = true; };
  }, [messageId]);

  if (loading) {
    return (
      <div className="space-y-4 animate-fade-in-up">
        <div>
          <h1 className="text-lg font-semibold">Delivery Log</h1>
          <p className="text-sm text-text-muted mt-0.5">Loading...</p>
        </div>
      </div>
    );
  }

  if (error || !message) {
    return (
      <div className="space-y-4 animate-fade-in-up">
        <div>
          <h1 className="text-lg font-semibold">Delivery Log</h1>
          <p className="text-sm text-danger mt-0.5">{error || "Message not found"}</p>
        </div>
        <Link
          to="/messages"
          className="inline-flex items-center gap-1.5 text-xs font-medium px-3 py-1.5 rounded-lg border border-border bg-surface-2 text-text-secondary hover:text-text-primary transition-colors"
        >
          <ArrowLeft className="w-3 h-3" />
          Back to Messages
        </Link>
      </div>
    );
  }

  let parsedPayload: unknown = message.payload;
  try {
    parsedPayload = JSON.parse(message.payload);
  } catch { /* keep as string */ }

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between animate-fade-in-up">
        <div>
          <div className="flex items-center gap-2 mb-1">
            <Link to="/messages" className="text-text-muted hover:text-text-primary transition-colors">
              <ArrowLeft className="w-4 h-4" />
            </Link>
            <h1 className="text-lg font-semibold">Delivery Log</h1>
          </div>
          <div className="flex items-center gap-2 text-sm">
            <span className="font-mono text-xs text-text-secondary">{message.id.slice(0, 16)}</span>
            <StatusBadge status={message.status} />
            <span className="text-text-muted">{message.eventType ?? "unknown"}</span>
            <span className="text-text-muted font-mono text-xs">{message.attemptCount}/{message.maxRetries}</span>
          </div>
        </div>
        {(message.status === "Failed" || message.status === "DeadLetter") && (
          <RetryButton messageId={message.id} />
        )}
      </div>

      {/* Message details grid */}
      <div className="rounded-xl border border-border bg-surface-1 p-4 animate-fade-in-up">
        <h2 className="text-sm font-semibold mb-3">Message Details</h2>
        <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-4 gap-3">
          <DetailItem icon={Globe} label="Endpoint" value={message.endpointUrl ?? message.endpointId} mono />
          <DetailItem icon={Tag} label="Event Type" value={message.eventType ?? "--"} />
          <DetailItem icon={Clock} label="Created" value={formatLocaleDateTime(message.createdAt)} />
          {message.deliveredAt && (
            <DetailItem icon={CheckCircle2} label="Delivered" value={formatLocaleDateTime(message.deliveredAt)} />
          )}
          {message.eventId && (
            <DetailItem icon={Hash} label="Event ID" value={message.eventId} mono />
          )}
        </div>
      </div>

      {/* Attempt timeline */}
      <div className="rounded-xl border border-border bg-surface-1 p-4 animate-fade-in-up">
        <h2 className="text-sm font-semibold mb-3">Attempt Timeline</h2>
        {message.attempts.length === 0 ? (
          <p className="text-sm text-text-muted py-4 text-center">No delivery attempts yet.</p>
        ) : (
          <div className="overflow-auto">
            <table className="w-full min-w-[640px]">
              <thead>
                <tr className="text-xs text-text-muted border-b border-border">
                  <th className="text-left font-medium px-3 py-2">#</th>
                  <th className="text-left font-medium px-3 py-2">Status</th>
                  <th className="text-left font-medium px-3 py-2">HTTP</th>
                  <th className="text-left font-medium px-3 py-2">Latency</th>
                  <th className="text-left font-medium px-3 py-2">Timestamp</th>
                  <th className="text-left font-medium px-3 py-2">Response / Error</th>
                </tr>
              </thead>
              <tbody className="text-sm">
                {message.attempts.map((attempt) => (
                  <tr key={attempt.id} className="border-t border-border-subtle">
                    <td className="px-3 py-2 font-mono text-xs text-text-muted">{attempt.attemptNumber}</td>
                    <td className="px-3 py-2">
                      <span className={`inline-flex items-center gap-1 text-xs font-medium px-1.5 py-0.5 rounded ${attempt.status === "Success" ? "text-success bg-success-soft" : "text-danger bg-danger-soft"}`}>
                        {attempt.status === "Success" ? <CheckCircle2 className="w-3 h-3" /> : <XCircle className="w-3 h-3" />}
                        {attempt.status}
                      </span>
                    </td>
                    <td className="px-3 py-2">
                      {attempt.statusCode ? (
                        <span className={`inline-block text-xs font-mono font-medium px-1.5 py-0.5 rounded ${attempt.statusCode >= 400 ? "text-danger bg-danger-soft" : "text-success bg-success-soft"}`}>
                          {attempt.statusCode}
                        </span>
                      ) : (
                        <span className="text-text-muted">--</span>
                      )}
                    </td>
                    <td className="px-3 py-2 font-mono text-xs text-text-secondary">{attempt.latencyMs}ms</td>
                    <td className="px-3 py-2 text-xs text-text-muted">{formatLocaleDateTime(attempt.createdAt)}</td>
                    <td className="px-3 py-2 text-xs text-text-muted max-w-[280px] truncate">
                      {attempt.error ?? attempt.responseBody ?? "--"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Request headers & payload */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {message.attempts.length > 0 && message.attempts[message.attempts.length - 1].requestHeaders && (
          <div className="rounded-xl border border-border bg-surface-1 p-4 animate-fade-in-up">
            <h2 className="text-sm font-semibold mb-3">Request Headers</h2>
            <PayloadViewer value={safeParseJson(message.attempts[message.attempts.length - 1].requestHeaders)} />
          </div>
        )}
        <div className="rounded-xl border border-border bg-surface-1 p-4 animate-fade-in-up">
          <h2 className="text-sm font-semibold mb-3">Payload</h2>
          <PayloadViewer value={parsedPayload} />
        </div>
      </div>
    </div>
  );
}

function DetailItem({ icon: Icon, label, value, mono }: { icon: React.ElementType; label: string; value: string; mono?: boolean }) {
  return (
    <div className="space-y-1">
      <div className="flex items-center gap-1.5 text-xs text-text-muted">
        <Icon className="w-3 h-3" />
        {label}
      </div>
      <p className={`text-sm text-text-primary truncate ${mono ? "font-mono text-xs" : ""}`} title={value}>
        {value}
      </p>
    </div>
  );
}

function safeParseJson(value: string | null | undefined): unknown {
  if (!value) return {};
  try {
    return JSON.parse(value);
  } catch {
    return value;
  }
}
