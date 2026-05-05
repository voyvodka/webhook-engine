import { useCallback, useEffect, useMemo, useState } from "react";
import { EndpointHealthBadge } from "../components/EndpointHealthBadge";
import { Modal } from "../components/Modal";
import { ConfirmModal } from "../components/ConfirmModal";
import { Select } from "../components/Select";
import { EventTypeSelect } from "../components/EventTypeSelect";
import { TransformSection } from "../components/TransformSection";
import {
  createDashboardEndpoint,
  deleteDashboardEndpoint,
  listApplications,
  listDashboardEventTypes,
  listEndpoints,
  setDashboardEndpointStatus,
  updateDashboardEndpoint
} from "../api/dashboardApi";
import type { ApplicationRow, EndpointRow, EventTypeSummary, Pagination } from "../types";
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
  Save
} from "lucide-react";
import { formatLocaleDate } from "../utils/dateTime";

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

const inputClasses = "w-full px-3 py-2 text-sm bg-surface-2 border border-border rounded-lg text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-1 focus:ring-accent/50 focus:border-accent/50 transition-colors";

export function EndpointsPage() {
  const [endpoints, setEndpoints] = useState<EndpointRow[]>([]);
  const [applications, setApplications] = useState<ApplicationRow[]>([]);
  const [eventTypesByApp, setEventTypesByApp] = useState<Record<string, EventTypeSummary[]>>({});
  const [pagination, setPagination] = useState<Pagination | null>(null);
  const [page, setPage] = useState(1);
  const [statusFilter, setStatusFilter] = useState("");
  const [appFilter, setAppFilter] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  // Create modal
  const [showCreate, setShowCreate] = useState(false);
  const [createAppId, setCreateAppId] = useState("");
  const [createUrl, setCreateUrl] = useState("");
  const [createDescription, setCreateDescription] = useState("");
  const [createFilterEventTypeIds, setCreateFilterEventTypeIds] = useState<string[]>([]);
  const [createTransformExpression, setCreateTransformExpression] = useState("");
  const [createTransformEnabled, setCreateTransformEnabled] = useState(false);
  const [creating, setCreating] = useState(false);

  // Edit modal
  const [editingEndpointId, setEditingEndpointId] = useState<string | null>(null);
  const [editUrl, setEditUrl] = useState("");
  const [editDescription, setEditDescription] = useState("");
  const [editFilterEventTypeIds, setEditFilterEventTypeIds] = useState<string[]>([]);
  const [editTransformExpression, setEditTransformExpression] = useState("");
  const [editTransformEnabled, setEditTransformEnabled] = useState(false);
  const [updating, setUpdating] = useState(false);

  // Confirm modal
  const [confirmState, setConfirmState] = useState<ConfirmState>(closedConfirm);

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

  const fetchEndpoints = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const result = await listEndpoints({ appId: appFilter || undefined, status: statusFilter || undefined, page, pageSize: 20 });
      setEndpoints(result.data);
      setPagination(result.pagination);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load endpoints");
    } finally {
      setLoading(false);
    }
  }, [appFilter, page, statusFilter]);

  useEffect(() => {
    Promise.resolve()
      .then(() => fetchEndpoints())
      .catch(() => { /* surfaced via fetchEndpoints' setError */ });
  }, [fetchEndpoints]);

  useEffect(() => {
    const fetchApplications = async () => {
      try {
        const result = await listApplications(1, 200);
        setApplications(result.data);
        if (result.data.length > 0) setCreateAppId((c) => c || result.data[0].id);
      } catch { /* no-op */ }
    };
    fetchApplications();
  }, []);

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

  const handleCreate = async () => {
    if (!createAppId || !createUrl.trim()) return;
    setCreating(true);
    setError("");
    try {
      const trimmedExpr = createTransformExpression.trim();
      await createDashboardEndpoint({
        appId: createAppId,
        url: createUrl.trim(),
        description: createDescription.trim() || undefined,
        filterEventTypes: createFilterEventTypeIds.length > 0 ? createFilterEventTypeIds : [],
        transformExpression: trimmedExpr || null,
        transformEnabled: createTransformEnabled && trimmedExpr.length > 0
      });
      resetCreateForm();
      setShowCreate(false);
      await fetchEndpoints();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to create endpoint");
    } finally {
      setCreating(false);
    }
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

  const handleUpdate = async () => {
    if (!editingEndpoint || !editUrl.trim()) return;
    setUpdating(true);
    setError("");
    try {
      const trimmedExpr = editTransformExpression.trim();
      await updateDashboardEndpoint(editingEndpoint.id, {
        url: editUrl.trim(),
        description: editDescription.trim() || undefined,
        filterEventTypes: editFilterEventTypeIds,
        transformExpression: trimmedExpr.length > 0 ? trimmedExpr : "",
        transformEnabled: editTransformEnabled && trimmedExpr.length > 0
      });
      cancelEdit();
      await fetchEndpoints();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to update endpoint");
    } finally {
      setUpdating(false);
    }
  };

  const requestToggleStatus = (endpoint: EndpointRow) => {
    const isDisabled = endpoint.status.toLowerCase() === "disabled";
    const action = isDisabled ? "enable" : "disable";
    setConfirmState({
      open: true,
      title: `${isDisabled ? "Enable" : "Disable"} Endpoint`,
      description: isDisabled
        ? "This endpoint will start receiving webhook deliveries again."
        : "This endpoint will stop receiving webhook deliveries until re-enabled.",
      confirmLabel: isDisabled ? "Enable" : "Disable",
      variant: isDisabled ? "default" : "danger",
      onConfirm: async () => {
        try {
          await setDashboardEndpointStatus(endpoint.id, isDisabled);
          await fetchEndpoints();
        } catch (e) {
          setError(e instanceof Error ? e.message : `Failed to ${action} endpoint`);
        }
      }
    });
  };

  const requestDelete = (endpoint: EndpointRow) => {
    setConfirmState({
      open: true,
      title: "Delete Endpoint",
      description: `Delete this endpoint? All pending messages for this destination will be cancelled. This action cannot be undone.`,
      confirmLabel: "Delete",
      variant: "danger",
      onConfirm: async () => {
        try {
          await deleteDashboardEndpoint(endpoint.id);
          await fetchEndpoints();
        } catch (e) {
          setError(e instanceof Error ? e.message : "Failed to delete endpoint");
        }
      }
    });
  };

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
      {error && (
        <div className="flex items-center gap-2 text-danger text-xs bg-danger-soft border border-danger/20 rounded-lg px-3 py-2 animate-fade-in-up">
          <AlertCircle className="w-3.5 h-3.5 shrink-0" />
          {error}
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

        {applications.length === 0 ? (
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
          <TransformSection
            enabled={createTransformEnabled}
            expression={createTransformExpression}
            onChange={(next) => {
              setCreateTransformEnabled(next.enabled);
              setCreateTransformExpression(next.expression);
            }}
          />
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
          <TransformSection
            enabled={editTransformEnabled}
            expression={editTransformExpression}
            onChange={(next) => {
              setEditTransformEnabled(next.enabled);
              setEditTransformExpression(next.expression);
            }}
          />
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
    </div>
  );
}
