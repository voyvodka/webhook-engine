import { lazy, Suspense, useCallback, useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { EndpointHealthBadge } from "../components/EndpointHealthBadge";
import { Modal } from "../components/Modal";
import { ConfirmModal } from "../components/ConfirmModal";
import { Select } from "../components/Select";
import { EventTypeSelect } from "../components/EventTypeSelect";
import { useDeliveryFeed } from "../hooks/useDeliveryFeed";
import {
  createDashboardEndpoint,
  deleteDashboardEndpoint,
  listApplications,
  listDashboardEventTypes,
  listEndpoints,
  setDashboardEndpointStatus,
  updateDashboardEndpoint
} from "../api/dashboardApi";
import type { EndpointRow, EventTypeSummary } from "../types";
import {
  Plus,
  Pencil,
  Trash2,
  ToggleLeft,
  ToggleRight,
  Filter,
  AlertCircle,
  X,
  ChevronLeft,
  ChevronRight,
  Save,
  Send
} from "lucide-react";
import { formatLocaleDate } from "../utils/dateTime";
import { inputClasses } from "../utils/styles";

// Lazy-load heavy CodeMirror components so the editor chunk only downloads
// when the user actually opens the create/edit or test modals — not on every
// Endpoints page visit. DPR-2 bundle consolidation pass.
const TransformSection = lazy(() =>
  import("../components/TransformSection").then((m) => ({ default: m.TransformSection }))
);
const EndpointTestModal = lazy(() =>
  import("../components/EndpointTestModal").then((m) => ({ default: m.EndpointTestModal }))
);

function toTitleCase(value: string): string {
  if (!value) return value;
  return value.charAt(0).toUpperCase() + value.slice(1).toLowerCase();
}

interface ConfirmState {
  open: boolean;
  title: string;
  description: string;
  confirmLabel: string;
  variant: "danger" | "default";
  onConfirm: () => void;
}

const closedConfirm: ConfirmState = {
  open: false, title: "", description: "", confirmLabel: "Confirm", variant: "default", onConfirm: () => {}
};

export function EndpointsPage() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [statusFilter, setStatusFilter] = useState("");
  const [appFilter, setAppFilter] = useState("");
  const [error, setError] = useState("");

  const endpointsQuery = useQuery({
    queryKey: ["endpoints", { page, appFilter, statusFilter }],
    queryFn: () => listEndpoints({
      appId: appFilter || undefined,
      status: statusFilter || undefined,
      page,
      pageSize: 20
    })
  });
  const endpoints = useMemo(() => endpointsQuery.data?.data ?? [], [endpointsQuery.data]);
  const pagination = endpointsQuery.data?.pagination ?? null;
  const loading = endpointsQuery.isLoading;

  const applicationsQuery = useQuery({
    queryKey: ["applications-all"],
    queryFn: () => listApplications(1, 200)
  });
  const applications = useMemo(() => applicationsQuery.data?.data ?? [], [applicationsQuery.data]);
  const applicationsLoading = applicationsQuery.isLoading;

  // Per-app event-type cache — populated lazily as the user opens the create
  // modal or starts editing a row. We hold a local map so the create / edit
  // forms have synchronous access to the list once it's fetched.
  const [eventTypesByApp, setEventTypesByApp] = useState<Record<string, EventTypeSummary[]>>({});

  const fetchError = endpointsQuery.error instanceof Error ? endpointsQuery.error.message : "";
  const displayError = error || fetchError;

  const invalidateEndpoints = () => queryClient.invalidateQueries({ queryKey: ["endpoints"] });

  // Create modal
  const [showCreate, setShowCreate] = useState(false);
  const [createAppId, setCreateAppId] = useState("");
  const [createUrl, setCreateUrl] = useState("");
  const [createDescription, setCreateDescription] = useState("");
  const [createFilterEventTypeIds, setCreateFilterEventTypeIds] = useState<string[]>([]);
  const [createTransformExpression, setCreateTransformExpression] = useState("");
  const [createTransformEnabled, setCreateTransformEnabled] = useState(false);

  // Edit modal
  const [editingEndpointId, setEditingEndpointId] = useState<string | null>(null);
  const [editUrl, setEditUrl] = useState("");
  const [editDescription, setEditDescription] = useState("");
  const [editFilterEventTypeIds, setEditFilterEventTypeIds] = useState<string[]>([]);
  const [editTransformExpression, setEditTransformExpression] = useState("");
  const [editTransformEnabled, setEditTransformEnabled] = useState(false);

  // Confirm modal
  const [confirmState, setConfirmState] = useState<ConfirmState>(closedConfirm);

  // Test modal — opens when the row's "Send" button is pressed
  const [testEndpointId, setTestEndpointId] = useState<string | null>(null);
  const [testEndpointUrl, setTestEndpointUrl] = useState<string | null>(null);

  const editingEndpoint = useMemo(
    () => endpoints.find((e) => e.id === editingEndpointId) ?? null,
    [editingEndpointId, endpoints]
  );

  const appOptions = useMemo(
    () => applications.map((a) => ({ value: a.id, label: a.name })),
    [applications]
  );

  const ensureEventTypesLoaded = useCallback(async (appId: string): Promise<EventTypeSummary[]> => {
    if (!appId || eventTypesByApp[appId]) return eventTypesByApp[appId] ?? [];
    const eventTypes = await listDashboardEventTypes(appId);
    setEventTypesByApp((prev) => ({ ...prev, [appId]: eventTypes }));
    return eventTypes;
  }, [eventTypesByApp]);

  // Default to the first application as the create-modal target once the
  // applications list lands. Microtask defer matches the codebase's existing
  // workaround for react-hooks/set-state-in-effect.
  useEffect(() => {
    if (applications.length === 0) return;
    void Promise.resolve().then(() => {
      setCreateAppId((c) => c || applications[0].id);
    });
  }, [applications]);

  // Replace the F7 lastHealthChange direct setState patch with a query
  // invalidation: the next refetch is the source of truth, dedup is automatic
  // across multiple pages opening the same query, and stale-on-reconnect
  // (DPR-1's reset to null) gets us a clean refresh. The "rows on other
  // pages pick up on next refresh" caveat from before now applies to ANY
  // open EndpointsPage instance — not just the one that received the event.
  const { lastHealthChange } = useDeliveryFeed();
  useEffect(() => {
    if (!lastHealthChange) return;
    void Promise.resolve().then(() => {
      queryClient.invalidateQueries({ queryKey: ["endpoints"] });
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lastHealthChange]);

  useEffect(() => {
    if (!showCreate || !createAppId) return;
    Promise.resolve()
      .then(() => ensureEventTypesLoaded(createAppId))
      .catch(() => { /* no-op — event types optional in create form */ });
  }, [createAppId, ensureEventTypesLoaded, showCreate]);

  // ── Handlers ──────────────────────────────────

  const handleFilterChange = (value: string) => { setStatusFilter(value); setPage(1); };
  const handleAppFilterChange = (value: string) => { setAppFilter(value); setPage(1); };

  const resetCreateForm = () => {
    setCreateUrl("");
    setCreateDescription("");
    setCreateFilterEventTypeIds([]);
    setCreateTransformExpression("");
    setCreateTransformEnabled(false);
  };

  const createMutation = useMutation({
    mutationFn: (vars: Parameters<typeof createDashboardEndpoint>[0]) => createDashboardEndpoint(vars),
    onSuccess: () => {
      resetCreateForm();
      setShowCreate(false);
      invalidateEndpoints();
    },
    onError: (e: unknown) => setError(e instanceof Error ? e.message : "Failed to create endpoint")
  });
  const creating = createMutation.isPending;

  const handleCreate = () => {
    if (!createAppId || !createUrl.trim()) return;
    setError("");
    const trimmedExpr = createTransformExpression.trim();
    createMutation.mutate({
      appId: createAppId,
      url: createUrl.trim(),
      description: createDescription.trim() || undefined,
      filterEventTypes: createFilterEventTypeIds.length > 0 ? createFilterEventTypeIds : [],
      transformExpression: trimmedExpr || null,
      transformEnabled: createTransformEnabled && trimmedExpr.length > 0
    });
    // TODO(DPR-3): createMutation.error is an ApiErrorException with
    // fieldErrors on 422 — wire url field error into the input once the
    // form is upgraded in the next pass.
  };

  const startEdit = async (endpoint: EndpointRow) => {
    setEditingEndpointId(endpoint.id);
    setEditUrl(endpoint.url);
    setEditDescription(endpoint.description ?? "");
    setEditTransformExpression(endpoint.transformExpression ?? "");
    setEditTransformEnabled(endpoint.transformEnabled ?? false);
    try {
      const knownEventTypes = await ensureEventTypesLoaded(endpoint.appId);
      const fallbackIds = knownEventTypes.filter((et) => endpoint.eventTypes.includes(et.name)).map((et) => et.id);
      setEditFilterEventTypeIds(endpoint.eventTypeIds ?? fallbackIds);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load event types");
    }
  };

  const cancelEdit = () => {
    setEditingEndpointId(null);
    setEditUrl("");
    setEditDescription("");
    setEditFilterEventTypeIds([]);
    setEditTransformExpression("");
    setEditTransformEnabled(false);
  };

  const updateMutation = useMutation({
    mutationFn: (vars: { id: string; payload: Parameters<typeof updateDashboardEndpoint>[1] }) =>
      updateDashboardEndpoint(vars.id, vars.payload),
    onSuccess: () => { cancelEdit(); invalidateEndpoints(); },
    onError: (e: unknown) => setError(e instanceof Error ? e.message : "Failed to update endpoint")
  });
  const updating = updateMutation.isPending;

  const handleUpdate = () => {
    if (!editingEndpoint || !editUrl.trim()) return;
    setError("");
    const trimmedExpr = editTransformExpression.trim();
    updateMutation.mutate({
      id: editingEndpoint.id,
      payload: {
        url: editUrl.trim(),
        description: editDescription.trim() || undefined,
        filterEventTypes: editFilterEventTypeIds,
        transformExpression: trimmedExpr.length > 0 ? trimmedExpr : "",
        transformEnabled: editTransformEnabled && trimmedExpr.length > 0
      }
    });
  };

  const toggleStatusMutation = useMutation({
    mutationFn: (vars: { id: string; enable: boolean }) => setDashboardEndpointStatus(vars.id, vars.enable),
    onSuccess: () => invalidateEndpoints(),
    onError: (e: unknown) => setError(e instanceof Error ? e.message : "Failed to toggle endpoint status")
  });

  const requestToggleStatus = (endpoint: EndpointRow) => {
    const isDisabled = endpoint.status.toLowerCase() === "disabled";
    setConfirmState({
      open: true,
      title: `${isDisabled ? "Enable" : "Disable"} Endpoint`,
      description: isDisabled
        ? "This endpoint will start receiving webhook deliveries again."
        : "This endpoint will stop receiving webhook deliveries until re-enabled.",
      confirmLabel: isDisabled ? "Enable" : "Disable",
      variant: isDisabled ? "default" : "danger",
      onConfirm: () => {
        setError("");
        toggleStatusMutation.mutate({ id: endpoint.id, enable: isDisabled });
      }
    });
  };

  const handleTest = (endpoint: EndpointRow) => {
    setTestEndpointId(endpoint.id);
    setTestEndpointUrl(endpoint.url);
  };

  const handleTestClose = useCallback(() => {
    setTestEndpointId(null);
    setTestEndpointUrl(null);
  }, []);

  const requestDelete = (endpoint: EndpointRow) => {
    setConfirmState({
      open: true,
      title: "Delete Endpoint",
      description: `Delete this endpoint? All pending messages for this destination will be cancelled. This action cannot be undone.`,
      confirmLabel: "Delete",
      variant: "danger",
      onConfirm: () => {
        setError("");
        deleteMutation.mutate(endpoint.id);
      }
    });
  };

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteDashboardEndpoint(id),
    onSuccess: () => invalidateEndpoints(),
    onError: (e: unknown) => setError(e instanceof Error ? e.message : "Failed to delete endpoint")
  });

  const createEventTypes = createAppId ? eventTypesByApp[createAppId] ?? [] : [];
  const editEventTypes = editingEndpoint ? eventTypesByApp[editingEndpoint.appId] ?? [] : [];

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between animate-fade-in-up">
        <div>
          <h1 className="text-lg font-semibold">Endpoints</h1>
          <p className="text-sm text-text-muted mt-0.5">Destination health and endpoint filters.</p>
        </div>
        <button
          onClick={() => setShowCreate(true)}
          disabled={applications.length === 0}
          className="inline-flex items-center gap-1.5 text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          <Plus className="w-3.5 h-3.5" />
          New Endpoint
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

      {/* Filters */}
      <div className="relative z-20 rounded-lg border border-border bg-surface-1 p-3 animate-fade-in-up">
        <div className="flex items-center gap-2 mb-2">
          <Filter className="w-3.5 h-3.5 text-text-muted" />
          <span className="text-xs font-medium text-text-secondary">Filters</span>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-2">
          <div>
            <span className="text-xs text-text-muted mb-1 block">Application</span>
            <Select
              value={appFilter}
              onChange={handleAppFilterChange}
              options={[{ value: "", label: "All" }, ...appOptions]}
              placeholder="All applications"
            />
          </div>
          <div>
            <span className="text-xs text-text-muted mb-1 block">Status</span>
            <Select
              value={statusFilter}
              onChange={handleFilterChange}
              options={[
                { value: "", label: "All" },
                { value: "Active", label: "Active" },
                { value: "Disabled", label: "Disabled" }
              ]}
              placeholder="All statuses"
            />
          </div>
        </div>
      </div>

      {/* Table */}
      <div className="rounded-xl border border-border bg-surface-1 animate-fade-in-up">
        <div className="flex items-center justify-between px-4 py-2.5 border-b border-border-subtle">
          <h2 className="text-sm font-semibold">Destination Endpoints</h2>
          <span className="text-xs text-text-muted">{pagination ? `${pagination.totalCount} total` : ""}</span>
        </div>

        {applicationsLoading ? (
          <div className="flex items-center justify-center py-12">
            <span className="text-sm text-text-muted">Loading...</span>
          </div>
        ) : applications.length === 0 ? (
          <p className="text-sm text-text-muted px-4 py-8 text-center">Create an application first before adding endpoints.</p>
        ) : loading ? (
          <div className="flex items-center justify-center py-12">
            <span className="text-sm text-text-muted">Loading...</span>
          </div>
        ) : endpoints.length === 0 ? (
          <p className="text-sm text-text-muted px-4 py-8 text-center">No endpoints found.</p>
        ) : (
          <>
            <div className="overflow-auto">
              <table className="w-full min-w-[900px]">
                <thead>
                  <tr className="text-xs text-text-muted border-b border-border">
                    <th className="text-left font-medium px-4 py-2">URL</th>
                    <th className="text-left font-medium px-4 py-2">App</th>
                    <th className="text-left font-medium px-4 py-2">Status</th>
                    <th className="text-left font-medium px-4 py-2">Health</th>
                    <th className="text-left font-medium px-4 py-2">Filters</th>
                    <th className="text-left font-medium px-4 py-2">Created</th>
                    <th className="text-right font-medium px-4 py-2">Actions</th>
                  </tr>
                </thead>
                <tbody className="text-sm">
                  {endpoints.map((ep) => (
                    <tr key={ep.id} className="border-t border-border-subtle hover:bg-surface-2/50 transition-colors">
                      <td className="px-4 py-2 font-mono text-xs text-text-secondary max-w-[260px] truncate" title={ep.url}>{ep.url}</td>
                      <td className="px-4 py-2 text-text-secondary">{ep.appName ?? "-"}</td>
                      <td className="px-4 py-2">
                        <span className={`inline-block text-xs font-medium px-1.5 py-0.5 rounded ${ep.status.toLowerCase() === "disabled" ? "text-text-muted bg-surface-3" : "text-success bg-success-soft"}`}>
                          {toTitleCase(ep.status)}
                        </span>
                      </td>
                      <td className="px-4 py-2"><EndpointHealthBadge state={ep.status} /></td>
                      <td className="px-4 py-2 text-xs text-text-muted max-w-[180px] truncate">
                        {ep.eventTypes.length > 0 ? ep.eventTypes.join(", ") : "All events"}
                      </td>
                      <td className="px-4 py-2 text-xs text-text-muted">{formatLocaleDate(ep.createdAt)}</td>
                      <td className="px-4 py-2">
                        <div className="flex items-center justify-end gap-1">
                          <button
                            onClick={() => handleTest(ep)}
                            disabled={ep.status.toLowerCase() === "disabled"}
                            className="p-1.5 rounded-md text-text-muted hover:text-accent hover:bg-accent-soft disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
                            title="Send test event"
                          >
                            <Send className="w-3.5 h-3.5" />
                          </button>
                          <button onClick={() => startEdit(ep)} className="p-1.5 rounded-md text-text-muted hover:text-accent hover:bg-accent-soft transition-colors" title="Edit">
                            <Pencil className="w-3.5 h-3.5" />
                          </button>
                          <button onClick={() => requestToggleStatus(ep)} className="p-1.5 rounded-md text-text-muted hover:text-warning hover:bg-warning-soft transition-colors" title={ep.status.toLowerCase() === "disabled" ? "Enable" : "Disable"}>
                            {ep.status.toLowerCase() === "disabled" ? <ToggleLeft className="w-3.5 h-3.5" /> : <ToggleRight className="w-3.5 h-3.5" />}
                          </button>
                          <button onClick={() => requestDelete(ep)} className="p-1.5 rounded-md text-text-muted hover:text-danger hover:bg-danger-soft transition-colors" title="Delete">
                            <Trash2 className="w-3.5 h-3.5" />
                          </button>
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

      {/* ── Create Modal ──────────────────────────── */}
      <Modal
        open={showCreate}
        onClose={() => { setShowCreate(false); resetCreateForm(); }}
        title="Create Endpoint"
        description="Configure a new webhook destination for an application."
        width="max-w-xl"
      >
        <div className="space-y-3">
          <div>
            <span className="text-xs font-medium text-text-secondary mb-1.5 block">Application</span>
            <Select
              value={createAppId}
              onChange={(v) => { setCreateAppId(v); setCreateFilterEventTypeIds([]); }}
              options={appOptions}
              placeholder="Select application"
            />
          </div>
          <label className="block">
            <span className="text-xs font-medium text-text-secondary mb-1.5 block">Endpoint URL</span>
            <input placeholder="https://example.com/webhook" value={createUrl} onChange={(e) => setCreateUrl(e.target.value)} className={inputClasses} />
          </label>
          <label className="block">
            <span className="text-xs font-medium text-text-secondary mb-1.5 block">Description (optional)</span>
            <input placeholder="Production payment handler" value={createDescription} onChange={(e) => setCreateDescription(e.target.value)} className={inputClasses} />
          </label>
          {createEventTypes.length > 0 && (
            <div>
              <span className="text-xs font-medium text-text-secondary mb-1.5 block">Filter Event Types (optional)</span>
              <EventTypeSelect eventTypes={createEventTypes} selected={createFilterEventTypeIds} onChange={setCreateFilterEventTypeIds} />
              <p className="text-xs text-text-muted mt-1">No selection = receives all events for this app.</p>
            </div>
          )}
          <Suspense fallback={<div className="p-4 text-xs text-text-muted">Loading editor...</div>}>
            <TransformSection
              enabled={createTransformEnabled}
              expression={createTransformExpression}
              onChange={(next) => {
                setCreateTransformEnabled(next.enabled);
                setCreateTransformExpression(next.expression);
              }}
            />
          </Suspense>
          <div className="flex items-center justify-end gap-2 pt-2">
            <button onClick={() => { setShowCreate(false); resetCreateForm(); }} className="text-xs font-medium px-3 py-1.5 rounded-lg border border-border bg-surface-2 text-text-secondary hover:text-text-primary hover:bg-surface-3 transition-colors">
              Cancel
            </button>
            <button
              onClick={handleCreate}
              disabled={creating || !createAppId || !createUrl.trim()}
              className="text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {creating ? "Creating..." : "Create Endpoint"}
            </button>
          </div>
        </div>
      </Modal>

      {/* ── Edit Modal ────────────────────────────── */}
      <Modal
        open={editingEndpointId !== null}
        onClose={cancelEdit}
        title="Edit Endpoint"
        description={editingEndpoint ? `Editing ${editingEndpoint.url}` : undefined}
        width="max-w-xl"
      >
        <div className="space-y-3">
          <label className="block">
            <span className="text-xs font-medium text-text-secondary mb-1.5 block">Endpoint URL</span>
            <input value={editUrl} onChange={(e) => setEditUrl(e.target.value)} className={inputClasses} />
          </label>
          <label className="block">
            <span className="text-xs font-medium text-text-secondary mb-1.5 block">Description</span>
            <input value={editDescription} onChange={(e) => setEditDescription(e.target.value)} className={inputClasses} />
          </label>
          {editEventTypes.length > 0 && (
            <div>
              <span className="text-xs font-medium text-text-secondary mb-1.5 block">Filter Event Types</span>
              <EventTypeSelect eventTypes={editEventTypes} selected={editFilterEventTypeIds} onChange={setEditFilterEventTypeIds} />
            </div>
          )}
          <Suspense fallback={<div className="p-4 text-xs text-text-muted">Loading editor...</div>}>
            <TransformSection
              enabled={editTransformEnabled}
              expression={editTransformExpression}
              onChange={(next) => {
                setEditTransformEnabled(next.enabled);
                setEditTransformExpression(next.expression);
              }}
            />
          </Suspense>
          <div className="flex items-center justify-end gap-2 pt-2">
            <button onClick={cancelEdit} className="text-xs font-medium px-3 py-1.5 rounded-lg border border-border bg-surface-2 text-text-secondary hover:text-text-primary hover:bg-surface-3 transition-colors">
              Cancel
            </button>
            <button
              onClick={handleUpdate}
              disabled={updating || !editUrl.trim()}
              className="inline-flex items-center gap-1.5 text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              <Save className="w-3 h-3" />
              {updating ? "Saving..." : "Save Changes"}
            </button>
          </div>
        </div>
      </Modal>

      {/* ── Confirm Modal ─────────────────────────── */}
      <ConfirmModal
        open={confirmState.open}
        onClose={() => setConfirmState(closedConfirm)}
        onConfirm={confirmState.onConfirm}
        title={confirmState.title}
        description={confirmState.description}
        confirmLabel={confirmState.confirmLabel}
        variant={confirmState.variant}
      />

      {/* ── Test Modal ────────────────────────────── */}
      <Suspense fallback={null}>
        <EndpointTestModal
          open={testEndpointId !== null}
          endpointId={testEndpointId}
          endpointUrl={testEndpointUrl}
          onClose={handleTestClose}
        />
      </Suspense>
    </div>
  );
}
