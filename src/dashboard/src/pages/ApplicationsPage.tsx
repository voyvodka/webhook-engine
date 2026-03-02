import { useEffect, useState, useCallback } from "react";
import {
  listApplications,
  createApplication,
  deleteApplication,
  listEndpoints,
  rotateApiKey,
  rotateSigningSecret
} from "../api/dashboardApi";
import { Modal } from "../components/Modal";
import { ConfirmModal } from "../components/ConfirmModal";
import type { ApplicationRow, EndpointRow, Pagination } from "../types";
import { formatLocaleDate } from "../utils/dateTime";
import {
  Plus,
  KeyRound,
  ShieldCheck,
  Trash2,
  Copy,
  Check,
  AlertCircle,
  X,
  ChevronLeft,
  ChevronRight
} from "lucide-react";

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    await navigator.clipboard.writeText(text);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <button
      onClick={handleCopy}
      className="p-1 rounded hover:bg-surface-3 text-text-muted hover:text-text-primary transition-colors"
      title="Copy"
    >
      {copied ? <Check className="w-3.5 h-3.5 text-success" /> : <Copy className="w-3.5 h-3.5" />}
    </button>
  );
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
  open: false,
  title: "",
  description: "",
  confirmLabel: "Confirm",
  variant: "default",
  onConfirm: () => {}
};

interface ApplicationEndpointStats {
  total: number;
  disabled: number;
  healthy: number;
  degraded: number;
  failed: number;
}

function summarizeEndpoints(total: number, endpoints: EndpointRow[]): ApplicationEndpointStats {
  let disabled = 0;
  let healthy = 0;
  let degraded = 0;
  let failed = 0;

  endpoints.forEach((endpoint) => {
    const normalizedStatus = endpoint.status.toLowerCase();

    if (normalizedStatus === "disabled") {
      disabled++;
      return;
    }

    if (normalizedStatus === "degraded") {
      degraded++;
      return;
    }

    if (normalizedStatus === "failed") {
      failed++;
      return;
    }

    healthy++;
  });

  return { total, disabled, healthy, degraded, failed };
}

export function ApplicationsPage() {
  const [apps, setApps] = useState<ApplicationRow[]>([]);
  const [appStats, setAppStats] = useState<Record<string, ApplicationEndpointStats>>({});
  const [pagination, setPagination] = useState<Pagination | null>(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [loadingStats, setLoadingStats] = useState(false);
  const [error, setError] = useState("");

  // Create modal
  const [showCreate, setShowCreate] = useState(false);
  const [createName, setCreateName] = useState("");
  const [createResult, setCreateResult] = useState<{ apiKey: string; signingSecret: string } | null>(null);
  const [creating, setCreating] = useState(false);

  // Secret reveal
  const [revealedKey, setRevealedKey] = useState<{ id: string; apiKey: string } | null>(null);
  const [revealedSecret, setRevealedSecret] = useState<{ id: string; signingSecret: string } | null>(null);

  // Confirm modal
  const [confirm, setConfirm] = useState<ConfirmState>(closedConfirm);

  const fetchAppStats = useCallback(async (rows: ApplicationRow[]) => {
    if (rows.length === 0) {
      setAppStats({});
      return;
    }

    setLoadingStats(true);
    try {
      const entries = await Promise.all(rows.map(async (app) => {
        const endpoints: EndpointRow[] = [];
        let currentPage = 1;
        let totalPages = 1;
        let totalCount = 0;

        do {
          const result = await listEndpoints({ appId: app.id, page: currentPage, pageSize: 200 });
          endpoints.push(...result.data);

          if (currentPage === 1) {
            totalPages = result.pagination.totalPages;
            totalCount = result.pagination.totalCount;
          }

          currentPage++;
        } while (currentPage <= totalPages);

        return [app.id, summarizeEndpoints(totalCount, endpoints)] as const;
      }));

      setAppStats(Object.fromEntries(entries));
    } catch {
      setAppStats({});
    } finally {
      setLoadingStats(false);
    }
  }, []);

  const fetchApps = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const result = await listApplications(page, 20);
      setApps(result.data);
      setPagination(result.pagination);
      await fetchAppStats(result.data);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load applications");
    } finally {
      setLoading(false);
    }
  }, [fetchAppStats, page]);

  useEffect(() => {
    fetchApps();
  }, [fetchApps]);

  const handleCreate = async () => {
    if (!createName.trim()) return;
    setCreating(true);
    try {
      const result = await createApplication(createName.trim());
      setCreateResult({ apiKey: result.apiKey, signingSecret: result.signingSecret });
      setCreateName("");
      setShowCreate(false);
      fetchApps();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to create application");
    } finally {
      setCreating(false);
    }
  };

  const requestDelete = (id: string, name: string) => {
    setConfirm({
      open: true,
      title: "Delete Application",
      description: `Delete "${name}"? This will also delete all its endpoints and messages. This action cannot be undone.`,
      confirmLabel: "Delete",
      variant: "danger",
      onConfirm: async () => {
        try {
          await deleteApplication(id);
          fetchApps();
        } catch (e) {
          setError(e instanceof Error ? e.message : "Failed to delete application");
        }
      }
    });
  };

  const requestRotateKey = (id: string) => {
    setConfirm({
      open: true,
      title: "Rotate API Key",
      description: "The current API key will stop working immediately. Make sure your integrations are ready for the new key.",
      confirmLabel: "Rotate Key",
      variant: "default",
      onConfirm: async () => {
        try {
          const result = await rotateApiKey(id);
          setRevealedKey({ id, apiKey: result.apiKey });
        } catch (e) {
          setError(e instanceof Error ? e.message : "Failed to rotate key");
        }
      }
    });
  };

  const requestRotateSecret = (id: string) => {
    setConfirm({
      open: true,
      title: "Rotate Signing Secret",
      description: "Webhook consumers will need the new secret to verify signatures. Update all consumers before rotating.",
      confirmLabel: "Rotate Secret",
      variant: "default",
      onConfirm: async () => {
        try {
          const result = await rotateSigningSecret(id);
          setRevealedSecret({ id, signingSecret: result.signingSecret });
        } catch (e) {
          setError(e instanceof Error ? e.message : "Failed to rotate secret");
        }
      }
    });
  };

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between animate-fade-in-up">
        <div>
          <h1 className="text-lg font-semibold">Applications</h1>
          <p className="text-sm text-text-muted mt-0.5">Manage API keys, signing secrets, and endpoint fleets.</p>
        </div>
        <button
          onClick={() => setShowCreate(true)}
          className="inline-flex items-center gap-1.5 text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 transition-colors"
        >
          <Plus className="w-3.5 h-3.5" />
          New App
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

      {/* Create result banner */}
      {createResult && (
        <div className="rounded-lg border border-success/20 bg-success-soft p-4 space-y-2 animate-fade-in-up">
          <div className="flex items-center justify-between">
            <h3 className="text-sm font-semibold text-success">Application Created</h3>
            <button onClick={() => setCreateResult(null)} className="text-text-muted hover:text-text-primary"><X className="w-3.5 h-3.5" /></button>
          </div>
          <p className="text-xs text-text-secondary">Save these credentials -- they won't be shown again.</p>
          <div className="space-y-1.5">
            <div className="flex items-center gap-2 bg-surface-0 rounded-md px-3 py-1.5">
              <span className="text-xs text-text-muted w-20 shrink-0">API Key</span>
              <code className="text-xs font-mono text-text-primary flex-1 break-all">{createResult.apiKey}</code>
              <CopyButton text={createResult.apiKey} />
            </div>
            <div className="flex items-center gap-2 bg-surface-0 rounded-md px-3 py-1.5">
              <span className="text-xs text-text-muted w-20 shrink-0">Secret</span>
              <code className="text-xs font-mono text-text-primary flex-1 break-all">{createResult.signingSecret}</code>
              <CopyButton text={createResult.signingSecret} />
            </div>
          </div>
        </div>
      )}

      {/* Rotated key/secret banners */}
      {revealedKey && (
        <div className="rounded-lg border border-accent/20 bg-accent-soft p-3 animate-fade-in-up">
          <div className="flex items-center justify-between mb-1.5">
            <h3 className="text-sm font-semibold text-accent">New API Key</h3>
            <button onClick={() => setRevealedKey(null)} className="text-text-muted hover:text-text-primary"><X className="w-3.5 h-3.5" /></button>
          </div>
          <div className="flex items-center gap-2 bg-surface-0 rounded-md px-3 py-1.5">
            <code className="text-xs font-mono text-text-primary flex-1 break-all">{revealedKey.apiKey}</code>
            <CopyButton text={revealedKey.apiKey} />
          </div>
        </div>
      )}

      {revealedSecret && (
        <div className="rounded-lg border border-accent/20 bg-accent-soft p-3 animate-fade-in-up">
          <div className="flex items-center justify-between mb-1.5">
            <h3 className="text-sm font-semibold text-accent">New Signing Secret</h3>
            <button onClick={() => setRevealedSecret(null)} className="text-text-muted hover:text-text-primary"><X className="w-3.5 h-3.5" /></button>
          </div>
          <div className="flex items-center gap-2 bg-surface-0 rounded-md px-3 py-1.5">
            <code className="text-xs font-mono text-text-primary flex-1 break-all">{revealedSecret.signingSecret}</code>
            <CopyButton text={revealedSecret.signingSecret} />
          </div>
        </div>
      )}

      {/* Table */}
      <div className="rounded-xl border border-border bg-surface-1 animate-fade-in-up">
        {loading ? (
          <div className="flex items-center justify-center py-12">
            <span className="text-sm text-text-muted">Loading...</span>
          </div>
        ) : apps.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-12 text-center">
            <p className="text-sm text-text-muted">No applications yet.</p>
            <p className="text-xs text-text-muted mt-1">Create one to get started.</p>
          </div>
        ) : (
          <>
            <div className="overflow-auto">
              <table className="w-full min-w-[920px]">
                <thead>
                  <tr className="text-xs text-text-muted border-b border-border">
                    <th className="text-left font-medium px-4 py-2.5">Name</th>
                    <th className="text-left font-medium px-4 py-2.5">API Key Prefix</th>
                    <th className="text-left font-medium px-4 py-2.5">Status</th>
                    <th className="text-left font-medium px-4 py-2.5">Endpoints</th>
                    <th className="text-left font-medium px-4 py-2.5">Health</th>
                    <th className="text-left font-medium px-4 py-2.5">Created</th>
                    <th className="text-right font-medium px-4 py-2.5">Actions</th>
                  </tr>
                </thead>
                <tbody className="text-sm">
                  {apps.map((app) => {
                    const stats = appStats[app.id];

                    return (
                      <tr key={app.id} className="border-t border-border-subtle hover:bg-surface-2/50 transition-colors">
                        <td className="px-4 py-2.5 font-medium">{app.name}</td>
                        <td className="px-4 py-2.5 font-mono text-xs text-text-secondary">{app.apiKeyPrefix}</td>
                        <td className="px-4 py-2.5">
                          <span className={`inline-block text-xs font-medium px-1.5 py-0.5 rounded ${app.isActive ? "text-success bg-success-soft" : "text-danger bg-danger-soft"}`}>
                            {app.isActive ? "Active" : "Disabled"}
                          </span>
                        </td>
                        <td className="px-4 py-2.5 text-xs text-text-secondary">
                          {stats ? (
                            <div>
                              <span className="font-medium">{stats.total}</span>
                              {stats.disabled > 0 && <span className="text-text-muted"> ({stats.disabled} disabled)</span>}
                            </div>
                          ) : (
                            <span className="text-text-muted">{loadingStats ? "..." : "0"}</span>
                          )}
                        </td>
                        <td className="px-4 py-2.5 text-xs">
                          {stats ? (
                            <div className="flex items-center gap-1.5">
                              <span className="text-success">{stats.healthy} healthy</span>
                              <span className="text-warning">{stats.degraded} degraded</span>
                              <span className="text-danger">{stats.failed} failed</span>
                            </div>
                          ) : (
                            <span className="text-text-muted">--</span>
                          )}
                        </td>
                        <td className="px-4 py-2.5 text-xs text-text-muted">{formatLocaleDate(app.createdAt)}</td>
                        <td className="px-4 py-2.5">
                          <div className="flex items-center justify-end gap-1">
                            <button onClick={() => requestRotateKey(app.id)} className="p-1.5 rounded-md text-text-muted hover:text-accent hover:bg-accent-soft transition-colors" title="Rotate API Key">
                              <KeyRound className="w-3.5 h-3.5" />
                            </button>
                            <button onClick={() => requestRotateSecret(app.id)} className="p-1.5 rounded-md text-text-muted hover:text-accent hover:bg-accent-soft transition-colors" title="Rotate Signing Secret">
                              <ShieldCheck className="w-3.5 h-3.5" />
                            </button>
                            <button onClick={() => requestDelete(app.id, app.name)} className="p-1.5 rounded-md text-text-muted hover:text-danger hover:bg-danger-soft transition-colors" title="Delete">
                              <Trash2 className="w-3.5 h-3.5" />
                            </button>
                          </div>
                        </td>
                      </tr>
                    );
                  })}
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

      {/* Create Modal */}
      <Modal
        open={showCreate}
        onClose={() => { setShowCreate(false); setCreateName(""); }}
        title="Create Application"
        description="Give your application a name. API key and signing secret will be generated automatically."
      >
        <div className="space-y-3">
          <label className="block">
            <span className="text-xs font-medium text-text-secondary mb-1.5 block">Application Name</span>
            <input
              placeholder="My Webhook App"
              value={createName}
              onChange={(e) => setCreateName(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && handleCreate()}
              autoFocus
              className="w-full px-3 py-2 text-sm bg-surface-2 border border-border rounded-lg text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-1 focus:ring-accent/50 focus:border-accent/50 transition-colors"
            />
          </label>
          <div className="flex items-center justify-end gap-2 pt-1">
            <button
              onClick={() => { setShowCreate(false); setCreateName(""); }}
              className="text-xs font-medium px-3 py-1.5 rounded-lg border border-border bg-surface-2 text-text-secondary hover:text-text-primary hover:bg-surface-3 transition-colors"
            >
              Cancel
            </button>
            <button
              onClick={handleCreate}
              disabled={creating || !createName.trim()}
              className="text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {creating ? "Creating..." : "Create"}
            </button>
          </div>
        </div>
      </Modal>

      {/* Confirm Modal */}
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
