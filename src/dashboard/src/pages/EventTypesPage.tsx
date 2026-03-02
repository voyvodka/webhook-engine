import { useCallback, useEffect, useMemo, useState } from "react";
import { ConfirmModal } from "../components/ConfirmModal";
import { Modal } from "../components/Modal";
import { Select } from "../components/Select";
import {
  archiveDashboardEventType,
  createDashboardEventType,
  listApplications,
  listDashboardEventTypes,
  updateDashboardEventType
} from "../api/dashboardApi";
import type { ApplicationRow, EventTypeSummary } from "../types";
import {
  Plus,
  Pencil,
  Archive,
  Filter,
  AlertCircle,
  X,
  Save
} from "lucide-react";

const inputClasses = "w-full px-3 py-2 text-sm bg-surface-2 border border-border rounded-lg text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-1 focus:ring-accent/50 focus:border-accent/50 transition-colors";

interface ConfirmState {
  open: boolean;
  title: string;
  description: string;
  confirmLabel: string;
  variant: "danger" | "default";
  onConfirm: () => void;
}

const closedConfirm: ConfirmState = {
  open: false,
  title: "",
  description: "",
  confirmLabel: "Confirm",
  variant: "default",
  onConfirm: () => {}
};

export function EventTypesPage() {
  const [applications, setApplications] = useState<ApplicationRow[]>([]);
  const [selectedAppId, setSelectedAppId] = useState("");
  const [includeArchived, setIncludeArchived] = useState(false);
  const [eventTypes, setEventTypes] = useState<EventTypeSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [showCreate, setShowCreate] = useState(false);
  const [createName, setCreateName] = useState("");
  const [createDescription, setCreateDescription] = useState("");
  const [creating, setCreating] = useState(false);

  const [editingEventTypeId, setEditingEventTypeId] = useState<string | null>(null);
  const [editName, setEditName] = useState("");
  const [editDescription, setEditDescription] = useState("");
  const [updating, setUpdating] = useState(false);

  const [confirm, setConfirm] = useState<ConfirmState>(closedConfirm);

  const appOptions = useMemo(
    () => applications.map((app) => ({ value: app.id, label: app.name })),
    [applications]
  );

  const editingEventType = useMemo(
    () => eventTypes.find((et) => et.id === editingEventTypeId) ?? null,
    [editingEventTypeId, eventTypes]
  );

  const fetchEventTypes = useCallback(async () => {
    if (!selectedAppId) {
      setEventTypes([]);
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");
    try {
      const rows = await listDashboardEventTypes(selectedAppId, includeArchived);
      setEventTypes(rows);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load event types");
    } finally {
      setLoading(false);
    }
  }, [includeArchived, selectedAppId]);

  useEffect(() => {
    const fetchApplications = async () => {
      try {
        const result = await listApplications(1, 200);
        setApplications(result.data);
        if (result.data.length > 0) {
          setSelectedAppId((current) => current || result.data[0].id);
        }
      } catch (e) {
        setError(e instanceof Error ? e.message : "Failed to load applications");
      }
    };

    fetchApplications();
  }, []);

  useEffect(() => {
    fetchEventTypes();
  }, [fetchEventTypes]);

  const resetCreateForm = () => {
    setCreateName("");
    setCreateDescription("");
  };

  const cancelEdit = () => {
    setEditingEventTypeId(null);
    setEditName("");
    setEditDescription("");
  };

  const handleCreate = async () => {
    if (!selectedAppId || !createName.trim()) {
      return;
    }

    setCreating(true);
    setError("");
    try {
      await createDashboardEventType({
        appId: selectedAppId,
        name: createName.trim(),
        description: createDescription.trim() || undefined
      });

      setShowCreate(false);
      resetCreateForm();
      await fetchEventTypes();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to create event type");
    } finally {
      setCreating(false);
    }
  };

  const startEdit = (eventType: EventTypeSummary) => {
    setEditingEventTypeId(eventType.id);
    setEditName(eventType.name);
    setEditDescription(eventType.description ?? "");
  };

  const handleUpdate = async () => {
    if (!editingEventType || !editName.trim()) {
      return;
    }

    setUpdating(true);
    setError("");
    try {
      await updateDashboardEventType(editingEventType.id, {
        name: editName.trim(),
        description: editDescription.trim() || undefined
      });

      cancelEdit();
      await fetchEventTypes();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to update event type");
    } finally {
      setUpdating(false);
    }
  };

  const requestArchive = (eventType: EventTypeSummary) => {
    setConfirm({
      open: true,
      title: "Archive Event Type",
      description: `Archive "${eventType.name}"? Existing messages stay intact, but this type cannot be used for new sends.`,
      confirmLabel: "Archive",
      variant: "danger",
      onConfirm: async () => {
        try {
          await archiveDashboardEventType(eventType.id);
          await fetchEventTypes();
        } catch (e) {
          setError(e instanceof Error ? e.message : "Failed to archive event type");
        }
      }
    });
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between animate-fade-in-up">
        <div>
          <h1 className="text-lg font-semibold">Event Types</h1>
          <p className="text-sm text-text-muted mt-0.5">Define and manage event taxonomy per application.</p>
        </div>
        <button
          onClick={() => setShowCreate(true)}
          disabled={!selectedAppId}
          className="inline-flex items-center gap-1.5 text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
        >
          <Plus className="w-3.5 h-3.5" />
          New Event Type
        </button>
      </div>

      {error && (
        <div className="flex items-center gap-2 text-danger text-xs bg-danger-soft border border-danger/20 rounded-lg px-3 py-2 animate-fade-in-up">
          <AlertCircle className="w-3.5 h-3.5 shrink-0" />
          {error}
          <button onClick={() => setError("")} className="ml-auto text-danger/60 hover:text-danger">
            <X className="w-3 h-3" />
          </button>
        </div>
      )}

      <div className="relative z-20 rounded-lg border border-border bg-surface-1 p-3 animate-fade-in-up">
        <div className="flex items-center gap-2 mb-2">
          <Filter className="w-3.5 h-3.5 text-text-muted" />
          <span className="text-xs font-medium text-text-secondary">Filters</span>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-3 gap-2 items-end">
          <div>
            <span className="text-xs text-text-muted mb-1 block">Application</span>
            <Select
              value={selectedAppId}
              onChange={setSelectedAppId}
              options={appOptions}
              placeholder="Select application"
            />
          </div>

          <label className="inline-flex items-center gap-2 px-3 py-2 text-sm bg-surface-2 border border-border rounded-lg text-text-secondary">
            <input
              type="checkbox"
              checked={includeArchived}
              onChange={(e) => setIncludeArchived(e.target.checked)}
              className="rounded border-border bg-surface-0 text-accent focus:ring-accent/40"
            />
            Show archived
          </label>
        </div>
      </div>

      <div className="rounded-xl border border-border bg-surface-1 animate-fade-in-up">
        <div className="flex items-center justify-between px-4 py-2.5 border-b border-border-subtle">
          <h2 className="text-sm font-semibold">Application Event Types</h2>
          <span className="text-xs text-text-muted">{selectedAppId ? `${eventTypes.length} total` : "Select an application"}</span>
        </div>

        {!selectedAppId ? (
          <p className="text-sm text-text-muted px-4 py-8 text-center">Create an application first to manage event types.</p>
        ) : loading ? (
          <div className="flex items-center justify-center py-12">
            <span className="text-sm text-text-muted">Loading...</span>
          </div>
        ) : eventTypes.length === 0 ? (
          <p className="text-sm text-text-muted px-4 py-8 text-center">No event types found.</p>
        ) : (
          <div className="overflow-auto">
            <table className="w-full min-w-[720px]">
              <thead>
                <tr className="text-xs text-text-muted border-b border-border">
                  <th className="text-left font-medium px-4 py-2">Name</th>
                  <th className="text-left font-medium px-4 py-2">Description</th>
                  <th className="text-left font-medium px-4 py-2">Status</th>
                  <th className="text-left font-medium px-4 py-2">Created</th>
                  <th className="text-right font-medium px-4 py-2">Actions</th>
                </tr>
              </thead>
              <tbody className="text-sm">
                {eventTypes.map((eventType) => (
                  <tr key={eventType.id} className="border-t border-border-subtle hover:bg-surface-2/50 transition-colors">
                    <td className="px-4 py-2 font-medium">{eventType.name}</td>
                    <td className="px-4 py-2 text-text-secondary max-w-[320px] truncate" title={eventType.description ?? undefined}>
                      {eventType.description ?? "--"}
                    </td>
                    <td className="px-4 py-2">
                      <span
                        className={`inline-block text-xs font-medium px-1.5 py-0.5 rounded ${
                          eventType.isArchived
                            ? "text-text-muted bg-surface-3"
                            : "text-success bg-success-soft"
                        }`}
                      >
                        {eventType.isArchived ? "Archived" : "Active"}
                      </span>
                    </td>
                    <td className="px-4 py-2 text-xs text-text-muted">{new Date(eventType.createdAt).toLocaleDateString()}</td>
                    <td className="px-4 py-2">
                      <div className="flex items-center justify-end gap-1">
                        {!eventType.isArchived && (
                          <button
                            onClick={() => startEdit(eventType)}
                            className="p-1.5 rounded-md text-text-muted hover:text-accent hover:bg-accent-soft transition-colors"
                            title="Edit"
                          >
                            <Pencil className="w-3.5 h-3.5" />
                          </button>
                        )}
                        {!eventType.isArchived && (
                          <button
                            onClick={() => requestArchive(eventType)}
                            className="p-1.5 rounded-md text-text-muted hover:text-danger hover:bg-danger-soft transition-colors"
                            title="Archive"
                          >
                            <Archive className="w-3.5 h-3.5" />
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <Modal
        open={showCreate}
        onClose={() => {
          setShowCreate(false);
          resetCreateForm();
        }}
        title="Create Event Type"
        description="Create a reusable event identifier for an application."
      >
        <div className="space-y-3">
          <label className="block">
            <span className="text-xs font-medium text-text-secondary mb-1.5 block">Name</span>
            <input
              placeholder="order.created"
              value={createName}
              onChange={(e) => setCreateName(e.target.value)}
              className={inputClasses}
            />
          </label>

          <label className="block">
            <span className="text-xs font-medium text-text-secondary mb-1.5 block">Description (optional)</span>
            <input
              placeholder="Fires when a new order is created"
              value={createDescription}
              onChange={(e) => setCreateDescription(e.target.value)}
              className={inputClasses}
            />
          </label>

          <div className="flex items-center justify-end gap-2 pt-1">
            <button
              onClick={() => {
                setShowCreate(false);
                resetCreateForm();
              }}
              className="text-xs font-medium px-3 py-1.5 rounded-lg border border-border bg-surface-2 text-text-secondary hover:text-text-primary hover:bg-surface-3 transition-colors"
            >
              Cancel
            </button>
            <button
              onClick={handleCreate}
              disabled={creating || !createName.trim()}
              className="text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {creating ? "Creating..." : "Create Event Type"}
            </button>
          </div>
        </div>
      </Modal>

      <Modal
        open={editingEventTypeId !== null}
        onClose={cancelEdit}
        title="Edit Event Type"
        description={editingEventType ? `Editing ${editingEventType.name}` : undefined}
      >
        <div className="space-y-3">
          <label className="block">
            <span className="text-xs font-medium text-text-secondary mb-1.5 block">Name</span>
            <input value={editName} onChange={(e) => setEditName(e.target.value)} className={inputClasses} />
          </label>

          <label className="block">
            <span className="text-xs font-medium text-text-secondary mb-1.5 block">Description</span>
            <input value={editDescription} onChange={(e) => setEditDescription(e.target.value)} className={inputClasses} />
          </label>

          <div className="flex items-center justify-end gap-2 pt-1">
            <button
              onClick={cancelEdit}
              className="text-xs font-medium px-3 py-1.5 rounded-lg border border-border bg-surface-2 text-text-secondary hover:text-text-primary hover:bg-surface-3 transition-colors"
            >
              Cancel
            </button>
            <button
              onClick={handleUpdate}
              disabled={updating || !editName.trim()}
              className="inline-flex items-center gap-1.5 text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              <Save className="w-3 h-3" />
              {updating ? "Saving..." : "Save Changes"}
            </button>
          </div>
        </div>
      </Modal>

      <ConfirmModal
        open={confirm.open}
        onClose={() => setConfirm(closedConfirm)}
        onConfirm={confirm.onConfirm}
        title={confirm.title}
        description={confirm.description}
        confirmLabel={confirm.confirmLabel}
        variant={confirm.variant}
      />
    </div>
  );
}
