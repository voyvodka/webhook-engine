import { useCallback, useEffect, useReducer } from "react";
import type { JSX } from "react";
import type { PortalCapability, PortalEndpointSummary } from "../types.js";
import type { PortalClient, PortalPagination } from "../api/createPortalClient.js";

interface EndpointListProps {
  client: PortalClient;
  appId: string;
  capabilities: PortalCapability[];
  onEditEndpoint: (endpoint: PortalEndpointSummary) => void;
  onNewEndpoint: () => void;
}

interface ListState {
  endpoints: PortalEndpointSummary[];
  pagination: PortalPagination | null;
  page: number;
  loading: boolean;
  error: string | null;
  togglingId: string | null;
  deletingId: string | null;
}

type ListAction =
  | { type: "FETCH_START" }
  | { type: "FETCH_SUCCESS"; endpoints: PortalEndpointSummary[]; pagination: PortalPagination }
  | { type: "FETCH_ERROR"; message: string }
  | { type: "SET_PAGE"; page: number }
  | { type: "TOGGLE_START"; id: string }
  | { type: "TOGGLE_DONE"; id: string; isActive: boolean }
  | { type: "TOGGLE_ERROR"; id: string }
  | { type: "DELETE_START"; id: string }
  | { type: "DELETE_DONE"; id: string }
  | { type: "DELETE_ERROR"; id: string };

function listReducer(state: ListState, action: ListAction): ListState {
  switch (action.type) {
    case "FETCH_START":
      return { ...state, loading: true, error: null };
    case "FETCH_SUCCESS":
      return {
        ...state,
        loading: false,
        endpoints: action.endpoints,
        pagination: action.pagination,
      };
    case "FETCH_ERROR":
      return { ...state, loading: false, error: action.message };
    case "SET_PAGE":
      return { ...state, page: action.page };
    case "TOGGLE_START":
      return { ...state, togglingId: action.id };
    case "TOGGLE_DONE":
      return {
        ...state,
        togglingId: null,
        endpoints: state.endpoints.map((e) =>
          e.id === action.id ? { ...e, isActive: action.isActive } : e,
        ),
      };
    case "TOGGLE_ERROR":
      return { ...state, togglingId: null };
    case "DELETE_START":
      return { ...state, deletingId: action.id };
    case "DELETE_DONE":
      return {
        ...state,
        deletingId: null,
        endpoints: state.endpoints.filter((e) => e.id !== action.id),
      };
    case "DELETE_ERROR":
      return { ...state, deletingId: null };
  }
}

const PAGE_SIZE = 20;

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

function truncateUrl(url: string, max = 48): string {
  if (url.length <= max) return url;
  return url.slice(0, max) + "…";
}

export function EndpointList({
  client,
  capabilities,
  onEditEndpoint,
  onNewEndpoint,
}: EndpointListProps): JSX.Element {
  const canWrite = capabilities.includes("endpoints:write");

  const [state, dispatch] = useReducer(listReducer, {
    endpoints: [],
    pagination: null,
    page: 1,
    loading: true,
    error: null,
    togglingId: null,
    deletingId: null,
  });

  const fetchPage = useCallback(
    async (page: number) => {
      dispatch({ type: "FETCH_START" });
      try {
        const result = await client.listEndpoints({ page, pageSize: PAGE_SIZE });
        dispatch({
          type: "FETCH_SUCCESS",
          endpoints: result.data,
          pagination: result.pagination,
        });
      } catch (err) {
        dispatch({
          type: "FETCH_ERROR",
          message: err instanceof Error ? err.message : "Failed to load endpoints.",
        });
      }
    },
    [client],
  );

  useEffect(() => {
    void fetchPage(state.page);
  }, [fetchPage, state.page]);

  const handleToggle = async (endpoint: PortalEndpointSummary) => {
    dispatch({ type: "TOGGLE_START", id: endpoint.id });
    try {
      if (endpoint.isActive) {
        await client.disableEndpoint(endpoint.id);
        dispatch({ type: "TOGGLE_DONE", id: endpoint.id, isActive: false });
      } else {
        await client.enableEndpoint(endpoint.id);
        dispatch({ type: "TOGGLE_DONE", id: endpoint.id, isActive: true });
      }
    } catch {
      dispatch({ type: "TOGGLE_ERROR", id: endpoint.id });
    }
  };

  const handleDelete = async (endpoint: PortalEndpointSummary) => {
    if (!window.confirm(`Delete endpoint "${endpoint.url}"? This cannot be undone.`)) return;
    dispatch({ type: "DELETE_START", id: endpoint.id });
    try {
      await client.deleteEndpoint(endpoint.id);
      dispatch({ type: "DELETE_DONE", id: endpoint.id });
    } catch {
      dispatch({ type: "DELETE_ERROR", id: endpoint.id });
    }
  };

  const totalPages =
    state.pagination ? Math.max(1, Math.ceil(state.pagination.total / PAGE_SIZE)) : 1;

  if (state.loading && state.endpoints.length === 0) {
    return (
      <div className="animate-pulse space-y-3 p-4">
        {[1, 2, 3].map((i) => (
          <div key={i} className="h-12 rounded-lg bg-whe-bg-3" />
        ))}
      </div>
    );
  }

  if (state.error) {
    return (
      <div className="flex flex-col items-center gap-4 py-16 text-center">
        <p className="text-whe-danger text-sm">{state.error}</p>
        <button
          type="button"
          onClick={() => void fetchPage(state.page)}
          className="rounded-md bg-whe-accent px-4 py-2 text-sm font-medium text-whe-bg-0 hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-whe-accent"
        >
          Retry
        </button>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h2 className="text-base font-semibold text-whe-text-primary">Endpoints</h2>
        {canWrite && (
          <button
            type="button"
            onClick={onNewEndpoint}
            className="inline-flex items-center gap-1.5 rounded-md bg-whe-accent px-3 py-1.5 text-sm font-medium text-whe-bg-0 hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-whe-accent"
          >
            <span aria-hidden="true">+</span>
            New endpoint
          </button>
        )}
      </div>

      {/* Empty state */}
      {state.endpoints.length === 0 ? (
        <div className="flex flex-col items-center gap-4 rounded-xl border border-whe-border bg-whe-bg-2 py-16 text-center">
          <p className="text-sm text-whe-text-secondary">No endpoints yet.</p>
          {canWrite && (
            <button
              type="button"
              onClick={onNewEndpoint}
              className="rounded-md bg-whe-accent px-4 py-2 text-sm font-medium text-whe-bg-0 hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-whe-accent"
            >
              Create your first endpoint
            </button>
          )}
        </div>
      ) : (
        <>
          {/* Table */}
          <div className="overflow-x-auto rounded-xl border border-whe-border bg-whe-bg-2">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-whe-border text-left text-xs text-whe-text-muted">
                  <th className="px-4 py-3 font-medium">URL</th>
                  <th className="hidden px-4 py-3 font-medium sm:table-cell">Description</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                  <th className="hidden px-4 py-3 font-medium md:table-cell">Event types</th>
                  <th className="hidden px-4 py-3 font-medium lg:table-cell">Created</th>
                  {canWrite && <th className="px-4 py-3" />}
                </tr>
              </thead>
              <tbody>
                {state.endpoints.map((endpoint) => {
                  const isToggling = state.togglingId === endpoint.id;
                  const isDeleting = state.deletingId === endpoint.id;
                  return (
                    <tr
                      key={endpoint.id}
                      className="group cursor-pointer border-b border-whe-border-subtle last:border-0 hover:bg-whe-bg-3 transition-colors"
                      onClick={() => onEditEndpoint(endpoint)}
                    >
                      <td className="px-4 py-3 font-mono text-xs text-whe-text-primary">
                        {truncateUrl(endpoint.url)}
                      </td>
                      <td className="hidden px-4 py-3 text-whe-text-secondary sm:table-cell">
                        {endpoint.description ?? (
                          <span className="text-whe-text-muted">—</span>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        {endpoint.isActive ? (
                          <span className="inline-flex items-center gap-1 rounded-full bg-whe-success-soft px-2 py-0.5 text-xs font-medium text-whe-success">
                            <span className="h-1.5 w-1.5 rounded-full bg-whe-success" aria-hidden="true" />
                            Active
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-1 rounded-full bg-whe-bg-3 px-2 py-0.5 text-xs font-medium text-whe-text-muted">
                            <span className="h-1.5 w-1.5 rounded-full bg-whe-text-muted" aria-hidden="true" />
                            Disabled
                          </span>
                        )}
                      </td>
                      <td className="hidden px-4 py-3 text-whe-text-secondary md:table-cell">
                        {endpoint.filterEventTypes.length === 0 ? (
                          <span className="text-whe-text-muted">All</span>
                        ) : (
                          <span>{endpoint.filterEventTypes.length} filter{endpoint.filterEventTypes.length !== 1 ? "s" : ""}</span>
                        )}
                      </td>
                      <td className="hidden px-4 py-3 text-whe-text-muted lg:table-cell">
                        {formatDate(endpoint.createdAt)}
                      </td>
                      {canWrite && (
                        <td
                          className="px-4 py-3"
                          onClick={(e) => e.stopPropagation()}
                        >
                          <div className="flex items-center gap-2 opacity-0 group-hover:opacity-100 focus-within:opacity-100 transition-opacity">
                            <button
                              type="button"
                              disabled={isToggling || isDeleting}
                              onClick={() => void handleToggle(endpoint)}
                              className="rounded px-2 py-1 text-xs text-whe-text-secondary hover:text-whe-text-primary focus:outline-none focus-visible:ring-1 focus-visible:ring-whe-accent disabled:opacity-50"
                              aria-label={endpoint.isActive ? "Disable endpoint" : "Enable endpoint"}
                            >
                              {isToggling ? "…" : endpoint.isActive ? "Disable" : "Enable"}
                            </button>
                            <button
                              type="button"
                              disabled={isToggling || isDeleting}
                              onClick={() => void handleDelete(endpoint)}
                              className="rounded px-2 py-1 text-xs text-whe-danger hover:text-whe-danger focus:outline-none focus-visible:ring-1 focus-visible:ring-whe-danger disabled:opacity-50"
                              aria-label="Delete endpoint"
                            >
                              {isDeleting ? "…" : "Delete"}
                            </button>
                          </div>
                        </td>
                      )}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          {totalPages > 1 && (
            <div className="flex items-center justify-between text-sm text-whe-text-secondary">
              <button
                type="button"
                disabled={state.page <= 1 || state.loading}
                onClick={() => dispatch({ type: "SET_PAGE", page: state.page - 1 })}
                className="rounded px-3 py-1.5 hover:bg-whe-bg-3 disabled:opacity-40 focus:outline-none focus-visible:ring-1 focus-visible:ring-whe-accent"
              >
                Previous
              </button>
              <span className="text-xs text-whe-text-muted">
                Page {state.page} of {totalPages}
              </span>
              <button
                type="button"
                disabled={state.page >= totalPages || state.loading}
                onClick={() => dispatch({ type: "SET_PAGE", page: state.page + 1 })}
                className="rounded px-3 py-1.5 hover:bg-whe-bg-3 disabled:opacity-40 focus:outline-none focus-visible:ring-1 focus-visible:ring-whe-accent"
              >
                Next
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
