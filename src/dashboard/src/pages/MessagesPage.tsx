import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router";
import { RetryButton } from "../components/RetryButton";
import { Modal } from "../components/Modal";
import { Select } from "../components/Select";
import { StatusBadge } from "../components/StatusBadge";
import { useDeliveryFeed } from "../hooks/useDeliveryFeed";
import { listApplications, listEndpoints, listMessages, sendDashboardMessage } from "../api/dashboardApi";
import { formatLocaleDateTime } from "../utils/dateTime";
import { inputClasses } from "../utils/styles";
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
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [error, setError] = useState("");

  const [eventTypeFilter, setEventTypeFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [appFilter, setAppFilter] = useState("");
  const [endpointFilter, setEndpointFilter] = useState("");
  const [afterFilter, setAfterFilter] = useState("");
  const [beforeFilter, setBeforeFilter] = useState("");

  const [sendAppId, setSendAppId] = useState("");
  const [sendEventType, setSendEventType] = useState("");
  const [sendPayload, setSendPayload] = useState("{}");
  // Ayrı state: payload-parse hatasını genel error banner'ı ile karıştırmak
  // istemiyoruz — biri form-field-level, diğeri page-level / mutation-level.
  const [payloadError, setPayloadError] = useState("");
  const [sendIdempotencyKey, setSendIdempotencyKey] = useState("");
  const [lastSendAt, setLastSendAt] = useState(0);
  const [sendResult, setSendResult] = useState("");
  const [showSend, setShowSend] = useState(false);
  const { events, connected } = useDeliveryFeed(20);

  const messagesQuery = useQuery({
    queryKey: ["messages", { page, appFilter, endpointFilter, eventTypeFilter, statusFilter, afterFilter, beforeFilter }],
    queryFn: () => listMessages({
      appId: appFilter || undefined,
      endpointId: endpointFilter || undefined,
      eventType: eventTypeFilter || undefined,
      status: statusFilter || undefined,
      after: toIsoOrUndefined(afterFilter),
      before: toIsoOrUndefined(beforeFilter),
      page,
      pageSize: 20
    })
  });
  const messages = useMemo(() => messagesQuery.data?.data ?? [], [messagesQuery.data]);
  const pagination = messagesQuery.data?.pagination ?? null;
  const loading = messagesQuery.isLoading;

  const applicationsQuery = useQuery({
    queryKey: ["applications-all"],
    queryFn: () => listApplications(1, 200)
  });
  const applications = useMemo(() => applicationsQuery.data?.data ?? [], [applicationsQuery.data]);

  // Default the "send message" form to the first available app once it lands.
  useEffect(() => {
    if (applications.length === 0) return;
    void Promise.resolve().then(() => {
      setSendAppId((c) => c || applications[0].id);
    });
  }, [applications]);

  const endpointsQuery = useQuery({
    queryKey: ["endpoints-for-app", appFilter],
    queryFn: () => listEndpoints({ appId: appFilter, page: 1, pageSize: 200 }),
    enabled: !!appFilter
  });
  const endpointOptions = useMemo(() => {
    if (!appFilter || !endpointsQuery.data) return [];
    return endpointsQuery.data.data.map((endpoint) => ({ value: endpoint.id, label: endpoint.url }));
  }, [appFilter, endpointsQuery.data]);

  // Drop the endpoint filter when its app context changes / clears so we
  // don't carry a stale endpointId across an app filter swap.
  useEffect(() => {
    if (!appFilter) {
      void Promise.resolve().then(() => setEndpointFilter(""));
      return;
    }
    if (endpointOptions.length === 0) return;
    void Promise.resolve().then(() => {
      setEndpointFilter((current) => (endpointOptions.some((o) => o.value === current) ? current : ""));
    });
  }, [appFilter, endpointOptions]);

  const fetchError = messagesQuery.error instanceof Error ? messagesQuery.error.message : "";
  const displayError = error || fetchError;

  const invalidateMessages = () => queryClient.invalidateQueries({ queryKey: ["messages"] });

  // SignalR delivery events trigger a background invalidation rather than a
  // direct refetch — TanStack's staleTime + dedup makes the actual network
  // call only when the cache is stale, so a flood of events coalesces. The
  // 7-second setInterval poll is gone; staleTime + invalidate replaces it.
  useEffect(() => {
    if (events.length === 0) return;
    void Promise.resolve().then(() => invalidateMessages());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [events.length]);

  const sendMutation = useMutation({
    mutationFn: (payload: { appId: string; eventType: string; payload: unknown; idempotencyKey?: string }) =>
      sendDashboardMessage(payload),
    onSuccess: (result) => {
      setLastSendAt(Date.now());
      setSendResult(`Queued ${result.messageIds.length} message(s) for ${result.endpointCount} endpoint(s).`);
      invalidateMessages();
    },
    onError: (e: unknown) => setError(e instanceof Error ? e.message : "Failed to send message")
  });
  const sending = sendMutation.isPending;

  const applyFilters = () => {
    // setPage triggers a key change on messagesQuery, which re-fetches
    // automatically. No direct fetch call needed.
    setPage(1);
  };

  const handleSendMessage = () => {
    if (!sendAppId || !sendEventType.trim()) return;
    const now = Date.now();
    if (now - lastSendAt < 800) return;

    let parsedPayload: unknown;
    try {
      parsedPayload = JSON.parse(sendPayload);
    } catch {
      // Field-level error stays anchored to the payload textarea instead of
      // bouncing into the page-wide error banner where it loses context.
      setPayloadError("Payload must be valid JSON");
      return;
    }

    setError("");
    setPayloadError("");
    setSendResult("");
    sendMutation.mutate({
      appId: sendAppId,
      eventType: sendEventType.trim(),
      payload: parsedPayload,
      idempotencyKey: sendIdempotencyKey.trim() || undefined
    });
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
      {displayError && (
        <div className="flex items-center gap-2 text-danger text-xs bg-danger-soft border border-danger/20 rounded-lg px-3 py-2 animate-fade-in-up">
          <AlertCircle className="w-3.5 h-3.5 shrink-0" />
          {displayError}
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
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-2">
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
                      <td className="px-4 py-2"><StatusBadge kind={msg.status} /></td>
                      <td className="px-4 py-2 text-xs text-text-muted font-mono">{msg.attemptCount}/{msg.maxRetries}</td>
                      <td className="px-4 py-2 text-xs text-text-muted">{formatLocaleDateTime(msg.createdAt)}</td>
                      <td className="px-4 py-2">
                        <div className="flex items-center justify-end gap-1">
                          <Link to={`/delivery-log/${msg.id}`} className="p-1.5 rounded-md text-text-muted hover:text-accent hover:bg-accent-soft transition-colors" title="View details">
                            <ExternalLink className="w-3.5 h-3.5" />
                          </Link>
                          {(msg.status === "Failed" || msg.status === "DeadLetter") && (
                            <RetryButton messageId={msg.id} onRetried={() => invalidateMessages()} />
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
                onChange={(e) => { setSendPayload(e.target.value); if (payloadError) setPayloadError(""); }}
                aria-invalid={payloadError ? "true" : undefined}
                className={`${inputClasses} font-mono text-xs resize-y ${payloadError ? "border-danger/60" : ""}`}
              />
              {payloadError && (
                <span className="block text-[11px] text-danger mt-1 font-mono">{payloadError}</span>
              )}
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
