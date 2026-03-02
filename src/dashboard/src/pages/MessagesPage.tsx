import { useEffect, useState, useCallback } from "react";
import { Link } from "react-router";
import { RetryButton } from "../components/RetryButton";
import { Modal } from "../components/Modal";
import { Select } from "../components/Select";
import { useDeliveryFeed } from "../hooks/useDeliveryFeed";
import { listApplications, listEndpoints, listMessages, sendDashboardMessage } from "../api/dashboardApi";
import type { ApplicationRow, MessageRow, MessageStatusType, Pagination } from "../types";
import {
  Send,
  Filter,
  ExternalLink,
  AlertCircle,
  X,
  Wifi,
  WifiOff,
  ChevronLeft,
  ChevronRight
} from "lucide-react";

function prettyStatus(status: MessageStatusType): string {
  return status === "DeadLetter" ? "Dead Letter" : status;
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
      {prettyStatus(status as MessageStatusType)}
    </span>
  );
}

const inputClasses = "w-full px-3 py-2 text-sm bg-surface-2 border border-border rounded-lg text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-1 focus:ring-accent/50 focus:border-accent/50 transition-colors";

const statusOptions = [
  { value: "", label: "All" },
  { value: "Pending", label: "Pending" },
  { value: "Sending", label: "Sending" },
  { value: "Delivered", label: "Delivered" },
  { value: "Failed", label: "Failed" },
  { value: "DeadLetter", label: "Dead Letter" }
];

function toIsoOrUndefined(localDateTime: string): string | undefined {
  if (!localDateTime) return undefined;
  const date = new Date(localDateTime);
  if (Number.isNaN(date.getTime())) return undefined;
  return date.toISOString();
}

export function MessagesPage() {
  const [messages, setMessages] = useState<MessageRow[]>([]);
  const [pagination, setPagination] = useState<Pagination | null>(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [eventTypeFilter, setEventTypeFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [appFilter, setAppFilter] = useState("");
  const [endpointFilter, setEndpointFilter] = useState("");
  const [afterFilter, setAfterFilter] = useState("");
  const [beforeFilter, setBeforeFilter] = useState("");

  const [applications, setApplications] = useState<ApplicationRow[]>([]);
  const [endpointOptions, setEndpointOptions] = useState<Array<{ value: string; label: string }>>([]);
  const [sendAppId, setSendAppId] = useState("");
  const [sendEventType, setSendEventType] = useState("");
  const [sendPayload, setSendPayload] = useState("{}");
  const [sendIdempotencyKey, setSendIdempotencyKey] = useState("");
  const [sending, setSending] = useState(false);
  const [lastSendAt, setLastSendAt] = useState(0);
  const [sendResult, setSendResult] = useState("");
  const [showSend, setShowSend] = useState(false);
  const { events, connected } = useDeliveryFeed(20);

  const fetchMessages = useCallback(async (showSpinner = true) => {
    if (showSpinner) setLoading(true);
    try {
      const result = await listMessages({
        appId: appFilter || undefined,
        endpointId: endpointFilter || undefined,
        eventType: eventTypeFilter || undefined,
        status: statusFilter || undefined,
        after: toIsoOrUndefined(afterFilter),
        before: toIsoOrUndefined(beforeFilter),
        page,
        pageSize: 20
      });
      setMessages(result.data);
      setPagination(result.pagination);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load messages");
    } finally {
      if (showSpinner) setLoading(false);
    }
  }, [afterFilter, appFilter, beforeFilter, endpointFilter, eventTypeFilter, page, statusFilter]);

  useEffect(() => { fetchMessages(); }, [fetchMessages]);

  useEffect(() => {
    if (events.length === 0) return;
    fetchMessages(false).catch(() => { /* no-op */ });
  }, [events.length, fetchMessages]);

  useEffect(() => {
    const interval = window.setInterval(() => { fetchMessages(false).catch(() => { /* no-op */ }); }, 7000);
    return () => window.clearInterval(interval);
  }, [fetchMessages]);

  useEffect(() => {
    const fetchApplications = async () => {
      try {
        const result = await listApplications(1, 200);
        setApplications(result.data);
        if (result.data.length > 0) setSendAppId((c) => c || result.data[0].id);
      } catch { /* no-op */ }
    };
    fetchApplications();
  }, []);

  useEffect(() => {
    const fetchEndpoints = async () => {
      if (!appFilter) {
        setEndpointOptions([]);
        setEndpointFilter("");
        return;
      }

      try {
        const result = await listEndpoints({ appId: appFilter, page: 1, pageSize: 200 });
        const options = result.data.map((endpoint) => ({
          value: endpoint.id,
          label: endpoint.url
        }));
        setEndpointOptions(options);
        setEndpointFilter((current) => (options.some((option) => option.value === current) ? current : ""));
      } catch {
        setEndpointOptions([]);
        setEndpointFilter("");
      }
    };

    fetchEndpoints();
  }, [appFilter]);

  const applyFilters = () => { setPage(1); fetchMessages(); };

  const handleSendMessage = async () => {
    if (!sendAppId || !sendEventType.trim()) return;
    const now = Date.now();
    if (now - lastSendAt < 800) return;

    let parsedPayload: unknown;
    try { parsedPayload = JSON.parse(sendPayload); } catch { setError("Payload must be valid JSON"); return; }

    setSending(true);
    setError("");
    setSendResult("");
    try {
      const result = await sendDashboardMessage({
        appId: sendAppId,
        eventType: sendEventType.trim(),
        payload: parsedPayload,
        idempotencyKey: sendIdempotencyKey.trim() || undefined
      });
      setLastSendAt(now);
      setSendResult(`Queued ${result.messageIds.length} message(s) for ${result.endpointCount} endpoint(s).`);
      await fetchMessages();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to send message");
    } finally {
      setSending(false);
    }
  };

  const appOptions = applications.map((a) => ({ value: a.id, label: a.name }));
  const endpointFilterOptions = [{ value: "", label: "All" }, ...endpointOptions];

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between animate-fade-in-up">
        <div>
          <h1 className="text-lg font-semibold">Messages</h1>
          <p className="text-sm text-text-muted mt-0.5">Filter deliveries, trigger retries, and send test messages.</p>
        </div>
        <button
          onClick={() => setShowSend(true)}
          className="inline-flex items-center gap-1.5 text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 transition-colors"
        >
          <Send className="w-3.5 h-3.5" />
          Send Test
        </button>
      </div>

      {/* Error */}
      {error && (
        <div className="flex items-center gap-2 text-danger text-xs bg-danger-soft border border-danger/20 rounded-lg px-3 py-2 animate-fade-in-up">
          <AlertCircle className="w-3.5 h-3.5 shrink-0" />
          {error}
          <button onClick={() => setError("")} className="ml-auto text-danger/60 hover:text-danger"><X className="w-3 h-3" /></button>
        </div>
      )}

      {/* Send result toast */}
      {sendResult && (
        <div className="flex items-center gap-2 text-success text-xs bg-success-soft border border-success/20 rounded-lg px-3 py-2 animate-fade-in-up">
          {sendResult}
          <button onClick={() => setSendResult("")} className="ml-auto text-success/60 hover:text-success"><X className="w-3 h-3" /></button>
        </div>
      )}

      {/* Filters */}
      <div className="relative z-20 rounded-lg border border-border bg-surface-1 p-3 animate-fade-in-up">
        <div className="flex items-center gap-2 mb-2">
          <Filter className="w-3.5 h-3.5 text-text-muted" />
          <span className="text-xs font-medium text-text-secondary">Filters</span>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-6 gap-2">
          <div>
            <span className="text-xs text-text-muted mb-1 block">Application</span>
            <Select
              value={appFilter}
              onChange={(value) => {
                setAppFilter(value);
                setEndpointFilter("");
              }}
              options={[{ value: "", label: "All" }, ...appOptions]}
              placeholder="All applications"
            />
          </div>
          <div>
            <span className="text-xs text-text-muted mb-1 block">Endpoint</span>
            <Select
              value={endpointFilter}
              onChange={setEndpointFilter}
              options={endpointFilterOptions}
              placeholder={appFilter ? "All endpoints" : "Select application"}
            />
          </div>
          <label className="block">
            <span className="text-xs text-text-muted mb-1 block">Event Type</span>
            <input placeholder="order.created" value={eventTypeFilter} onChange={(e) => setEventTypeFilter(e.target.value)} className={inputClasses} />
          </label>
          <div>
            <span className="text-xs text-text-muted mb-1 block">Status</span>
            <Select value={statusFilter} onChange={setStatusFilter} options={statusOptions} placeholder="All statuses" />
          </div>
          <label className="block">
            <span className="text-xs text-text-muted mb-1 block">After</span>
            <input type="datetime-local" value={afterFilter} onChange={(e) => setAfterFilter(e.target.value)} className={inputClasses} />
          </label>
          <label className="block">
            <span className="text-xs text-text-muted mb-1 block">Before</span>
            <input type="datetime-local" value={beforeFilter} onChange={(e) => setBeforeFilter(e.target.value)} className={inputClasses} />
          </label>
          <div className="flex items-end">
            <button onClick={applyFilters} className="text-xs font-medium px-3 py-2 rounded-lg border border-border bg-surface-2 text-text-secondary hover:text-text-primary hover:bg-surface-3 transition-colors">
              Apply
            </button>
          </div>
        </div>
      </div>

      {/* Messages table */}
      <div className="rounded-xl border border-border bg-surface-1 animate-fade-in-up">
        <div className="flex items-center justify-between px-4 py-2.5 border-b border-border-subtle">
          <h2 className="text-sm font-semibold">Recent Messages</h2>
          <span className="flex items-center gap-2 text-xs text-text-muted">
            {pagination ? `${pagination.totalCount} total` : ""}
            <span className="flex items-center gap-1">
              {connected ? <Wifi className="w-3 h-3 text-success" /> : <WifiOff className="w-3 h-3 text-text-muted" />}
              {connected ? "live" : "offline"}
            </span>
          </span>
        </div>

        {loading ? (
          <div className="flex items-center justify-center py-12">
            <span className="text-sm text-text-muted">Loading...</span>
          </div>
        ) : messages.length === 0 ? (
          <p className="text-sm text-text-muted px-4 py-8 text-center">No messages found.</p>
        ) : (
          <>
            <div className="overflow-auto">
              <table className="w-full min-w-[800px]">
                <thead>
                  <tr className="text-xs text-text-muted border-b border-border">
                    <th className="text-left font-medium px-4 py-2">Message ID</th>
                    <th className="text-left font-medium px-4 py-2">Event Type</th>
                    <th className="text-left font-medium px-4 py-2">Endpoint</th>
                    <th className="text-left font-medium px-4 py-2">Status</th>
                    <th className="text-left font-medium px-4 py-2">Attempts</th>
                    <th className="text-left font-medium px-4 py-2">Created</th>
                    <th className="text-right font-medium px-4 py-2">Actions</th>
                  </tr>
                </thead>
                <tbody className="text-sm">
                  {messages.map((msg) => (
                    <tr key={msg.id} className="border-t border-border-subtle hover:bg-surface-2/50 transition-colors">
                      <td className="px-4 py-2 font-mono text-xs text-text-secondary">{msg.id.slice(0, 12)}</td>
                      <td className="px-4 py-2 text-text-secondary">{msg.eventType ?? "--"}</td>
                      <td className="px-4 py-2 font-mono text-xs text-text-muted max-w-[180px] truncate" title={msg.endpointUrl ?? msg.endpointId}>
                        {msg.endpointUrl ?? msg.endpointId.slice(0, 12)}
                      </td>
                      <td className="px-4 py-2"><StatusBadge status={msg.status} /></td>
                      <td className="px-4 py-2 text-xs text-text-muted font-mono">{msg.attemptCount}/{msg.maxRetries}</td>
                      <td className="px-4 py-2 text-xs text-text-muted">{new Date(msg.createdAt).toLocaleString()}</td>
                      <td className="px-4 py-2">
                        <div className="flex items-center justify-end gap-1">
                          <Link to={`/delivery-log/${msg.id}`} className="p-1.5 rounded-md text-text-muted hover:text-accent hover:bg-accent-soft transition-colors" title="View details">
                            <ExternalLink className="w-3.5 h-3.5" />
                          </Link>
                          {(msg.status === "Failed" || msg.status === "DeadLetter") && (
                            <RetryButton messageId={msg.id} onRetried={fetchMessages} />
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {pagination && pagination.totalPages > 1 && (
              <div className="flex items-center justify-center gap-3 px-4 py-2.5 border-t border-border-subtle">
                <button disabled={!pagination.hasPrev} onClick={() => setPage(page - 1)} className="p-1 rounded-md text-text-muted hover:text-text-primary disabled:opacity-30 disabled:cursor-not-allowed transition-colors">
                  <ChevronLeft className="w-4 h-4" />
                </button>
                <span className="text-xs text-text-muted">{pagination.page} / {pagination.totalPages}</span>
                <button disabled={!pagination.hasNext} onClick={() => setPage(page + 1)} className="p-1 rounded-md text-text-muted hover:text-text-primary disabled:opacity-30 disabled:cursor-not-allowed transition-colors">
                  <ChevronRight className="w-4 h-4" />
                </button>
              </div>
            )}
          </>
        )}
      </div>

      {/* ── Send Test Modal ───────────────────────── */}
      <Modal
        open={showSend}
        onClose={() => { setShowSend(false); setSendResult(""); }}
        title="Send Test Message"
        description="Dispatch a test webhook event to all matching endpoints."
        width="max-w-xl"
      >
        {applications.length === 0 ? (
          <p className="text-sm text-text-muted">Create an application first.</p>
        ) : (
          <div className="space-y-3">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div>
                <span className="text-xs font-medium text-text-secondary mb-1.5 block">Application</span>
                <Select value={sendAppId} onChange={setSendAppId} options={appOptions} placeholder="Select application" />
              </div>
              <label className="block">
                <span className="text-xs font-medium text-text-secondary mb-1.5 block">Event Type</span>
                <input placeholder="order.created" value={sendEventType} onChange={(e) => setSendEventType(e.target.value)} className={inputClasses} />
              </label>
            </div>
            <label className="block">
              <span className="text-xs font-medium text-text-secondary mb-1.5 block">Idempotency Key (optional)</span>
              <input placeholder="order-123" value={sendIdempotencyKey} onChange={(e) => setSendIdempotencyKey(e.target.value)} className={inputClasses} />
            </label>
            <label className="block">
              <span className="text-xs font-medium text-text-secondary mb-1.5 block">Payload (JSON)</span>
              <textarea
                rows={5}
                value={sendPayload}
                onChange={(e) => setSendPayload(e.target.value)}
                className={`${inputClasses} font-mono text-xs resize-y`}
              />
            </label>
            {sendResult && <p className="text-xs text-success">{sendResult}</p>}
            <div className="flex items-center justify-end gap-2 pt-1">
              <button
                onClick={() => { setShowSend(false); setSendResult(""); }}
                className="text-xs font-medium px-3 py-1.5 rounded-lg border border-border bg-surface-2 text-text-secondary hover:text-text-primary hover:bg-surface-3 transition-colors"
              >
                Close
              </button>
              <button
                onClick={handleSendMessage}
                disabled={sending || !sendAppId || !sendEventType.trim()}
                className="inline-flex items-center gap-1.5 text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                <Send className="w-3 h-3" />
                {sending ? "Sending..." : "Send Message"}
              </button>
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
}
