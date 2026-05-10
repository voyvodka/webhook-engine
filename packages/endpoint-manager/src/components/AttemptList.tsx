import { Fragment, useCallback, useEffect, useReducer, useRef } from "react";
import type { JSX } from "react";
import type { PortalAttempt, PortalCapability, PortalEndpointSummary } from "../types.js";
import type { PortalClient, PortalPagination } from "../api/createPortalClient.js";

export interface AttemptListProps {
  client: PortalClient;
  endpoint: PortalEndpointSummary;
  capabilities: PortalCapability[];
  onClose: () => void;
}

interface AttemptListState {
  attempts: PortalAttempt[];
  pagination: PortalPagination | null;
  page: number;
  loading: boolean;
  error: string | null;
  expandedId: string | null;
}

type AttemptListAction =
  | { type: "FETCH_START" }
  | { type: "FETCH_SUCCESS"; attempts: PortalAttempt[]; pagination: PortalPagination }
  | { type: "FETCH_ERROR"; message: string }
  | { type: "SET_PAGE"; page: number }
  | { type: "TOGGLE_EXPAND"; id: string };

function attemptReducer(state: AttemptListState, action: AttemptListAction): AttemptListState {
  switch (action.type) {
    case "FETCH_START":
      return { ...state, loading: true, error: null };
    case "FETCH_SUCCESS":
      return {
        ...state,
        loading: false,
        attempts: action.attempts,
        pagination: action.pagination,
      };
    case "FETCH_ERROR":
      return { ...state, loading: false, error: action.message };
    case "SET_PAGE":
      return { ...state, page: action.page };
    case "TOGGLE_EXPAND":
      return { ...state, expandedId: state.expandedId === action.id ? null : action.id };
  }
}

const PAGE_SIZE = 20;

function formatRelativeTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  const seconds = Math.floor(diff / 1000);
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

function StatusBadge({ status }: { status: string }): JSX.Element {
  const isSuccess = status.toLowerCase() === "success";
  return (
    <span
      className={[
        "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium",
        isSuccess
          ? "bg-whe-success-soft text-whe-success"
          : "bg-whe-danger-soft text-whe-danger",
      ].join(" ")}
    >
      <span
        className={`h-1.5 w-1.5 rounded-full ${isSuccess ? "bg-whe-success" : "bg-whe-danger"}`}
        aria-hidden="true"
      />
      {isSuccess ? "Success" : "Failure"}
    </span>
  );
}

function SkeletonRow(): JSX.Element {
  return (
    <tr>
      <td className="px-4 py-3"><div className="h-4 w-20 animate-pulse rounded bg-whe-bg-3" /></td>
      <td className="px-4 py-3"><div className="h-4 w-16 animate-pulse rounded bg-whe-bg-3" /></td>
      <td className="px-4 py-3"><div className="h-4 w-10 animate-pulse rounded bg-whe-bg-3" /></td>
      <td className="px-4 py-3"><div className="h-4 w-12 animate-pulse rounded bg-whe-bg-3" /></td>
      <td className="hidden px-4 py-3 sm:table-cell"><div className="h-4 w-32 animate-pulse rounded bg-whe-bg-3" /></td>
    </tr>
  );
}

function TableHead(): JSX.Element {
  return (
    <tr className="border-b border-whe-border text-left text-xs text-whe-text-muted">
      <th className="px-4 py-3 font-medium">When</th>
      <th className="px-4 py-3 font-medium">Status</th>
      <th className="px-4 py-3 font-medium">HTTP</th>
      <th className="px-4 py-3 font-medium">Latency</th>
      <th className="hidden px-4 py-3 font-medium sm:table-cell">Error</th>
    </tr>
  );
}

function truncateUrl(url: string, max = 48): string {
  if (url.length <= max) return url;
  return url.slice(0, max) + "…";
}

export function AttemptList({
  client,
  endpoint,
  capabilities,
  onClose,
}: AttemptListProps): JSX.Element {
  const canRead = capabilities.includes("attempts:read");
  const overlayRef = useRef<HTMLDivElement>(null);
  const firstFocusableRef = useRef<HTMLButtonElement>(null);

  const [state, dispatch] = useReducer(attemptReducer, {
    attempts: [],
    pagination: null,
    page: 1,
    loading: true,
    error: null,
    expandedId: null,
  });

  // Focus trap and Escape key.
  useEffect(() => {
    firstFocusableRef.current?.focus();

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onClose();
        return;
      }
      if (e.key !== "Tab") return;

      const overlay = overlayRef.current;
      if (!overlay) return;
      const focusable = overlay.querySelectorAll<HTMLElement>(
        'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
      );
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (!first || !last) return;

      if (e.shiftKey) {
        if (document.activeElement === first) {
          e.preventDefault();
          last.focus();
        }
      } else {
        if (document.activeElement === last) {
          e.preventDefault();
          first.focus();
        }
      }
    };

    document.addEventListener("keydown", handleKeyDown);
    document.body.style.overflow = "hidden";

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      document.body.style.overflow = "";
    };
  }, [onClose]);

  const fetchPage = useCallback(
    async (page: number) => {
      dispatch({ type: "FETCH_START" });
      try {
        const result = await client.listAttempts(endpoint.id, { page, pageSize: PAGE_SIZE });
        dispatch({
          type: "FETCH_SUCCESS",
          attempts: result.data,
          pagination: result.pagination,
        });
      } catch (err) {
        dispatch({
          type: "FETCH_ERROR",
          message: err instanceof Error ? err.message : "Failed to load attempts.",
        });
      }
    },
    [client, endpoint.id],
  );

  useEffect(() => {
    if (canRead) {
      void fetchPage(state.page);
    } else {
      dispatch({
        type: "FETCH_SUCCESS",
        attempts: [],
        pagination: { page: 1, pageSize: PAGE_SIZE, total: 0 },
      });
    }
  }, [fetchPage, state.page, canRead]);

  const totalPages = state.pagination
    ? Math.max(1, Math.ceil(state.pagination.total / PAGE_SIZE))
    : 1;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        ref={overlayRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="whe-attempts-title"
        className="relative flex w-full max-w-3xl flex-col overflow-hidden rounded-2xl border border-whe-border bg-whe-bg-1 shadow-2xl max-h-[90dvh]"
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-whe-border px-6 py-4">
          <div>
            <h2 id="whe-attempts-title" className="text-base font-semibold text-whe-text-primary">
              Delivery attempts
            </h2>
            <p className="mt-0.5 text-xs text-whe-text-muted font-mono">
              {truncateUrl(endpoint.url)}
            </p>
          </div>
          <button
            ref={firstFocusableRef}
            type="button"
            onClick={onClose}
            className="rounded-md p-1 text-whe-text-muted hover:text-whe-text-primary focus:outline-none focus-visible:ring-2 focus-visible:ring-whe-accent"
            aria-label="Close"
          >
            <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true">
              <path d="M12 4L4 12M4 4l8 8" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
            </svg>
          </button>
        </div>

        {/* Body */}
        <div className="overflow-y-auto px-6 py-5">
          {/* Capability gate */}
          {!canRead ? (
            <div className="rounded-lg border border-whe-warning/30 bg-whe-warning-soft px-4 py-4 text-sm text-whe-warning">
              <p className="font-medium">Capability required</p>
              <p className="mt-1 text-xs text-whe-text-secondary">
                Your portal token does not include the{" "}
                <code className="rounded bg-whe-bg-3 px-1 py-0.5 font-mono text-xs">
                  attempts:read
                </code>{" "}
                capability. Contact the application operator to request access.
              </p>
            </div>
          ) : state.error ? (
            <div className="flex flex-col items-center gap-4 py-12 text-center">
              <p className="text-sm text-whe-danger">{state.error}</p>
              <button
                type="button"
                onClick={() => void fetchPage(state.page)}
                className="rounded-md bg-whe-accent px-4 py-2 text-sm font-medium text-whe-bg-0 hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-whe-accent"
              >
                Retry
              </button>
            </div>
          ) : state.loading ? (
            <div className="overflow-x-auto rounded-xl border border-whe-border bg-whe-bg-2">
              <table className="w-full text-sm">
                <thead>
                  <TableHead />
                </thead>
                <tbody>
                  <SkeletonRow />
                  <SkeletonRow />
                  <SkeletonRow />
                </tbody>
              </table>
            </div>
          ) : state.attempts.length === 0 ? (
            <div className="flex flex-col items-center gap-2 py-16 text-center">
              <p className="text-sm text-whe-text-muted">
                No delivery attempts recorded for this endpoint yet.
              </p>
            </div>
          ) : (
            <div className="flex flex-col gap-4">
              <div className="overflow-x-auto rounded-xl border border-whe-border bg-whe-bg-2">
                <table className="w-full text-sm">
                  <thead>
                    <TableHead />
                  </thead>
                  <tbody>
                    {state.attempts.map((attempt) => {
                      const isExpanded = state.expandedId === attempt.id;
                      return (
                        <Fragment key={attempt.id}>
                          <tr
                            className="cursor-pointer border-b border-whe-border-subtle last:border-0 hover:bg-whe-bg-3 transition-colors"
                            onClick={() => dispatch({ type: "TOGGLE_EXPAND", id: attempt.id })}
                          >
                            <td className="px-4 py-3">
                              <time
                                dateTime={attempt.createdAt}
                                title={attempt.createdAt}
                                className="text-xs text-whe-text-secondary"
                              >
                                {formatRelativeTime(attempt.createdAt)}
                              </time>
                            </td>
                            <td className="px-4 py-3">
                              <StatusBadge status={attempt.status} />
                            </td>
                            <td className="px-4 py-3 text-xs text-whe-text-secondary">
                              {attempt.statusCode ?? "—"}
                            </td>
                            <td className="px-4 py-3 text-xs text-whe-text-secondary">
                              {attempt.latencyMs} ms
                            </td>
                            <td className="hidden px-4 py-3 sm:table-cell">
                              {attempt.error ? (
                                <span
                                  className="text-xs text-whe-danger"
                                  title={attempt.error}
                                >
                                  {attempt.error.length > 60
                                    ? attempt.error.slice(0, 60) + "…"
                                    : attempt.error}
                                </span>
                              ) : (
                                <span className="text-xs text-whe-text-muted">—</span>
                              )}
                            </td>
                          </tr>
                          {isExpanded && (
                            <tr
                              key={`${attempt.id}-expanded`}
                              className="border-b border-whe-border-subtle last:border-0 bg-whe-bg-2"
                            >
                              <td colSpan={5} className="px-4 py-3">
                                <div className="rounded-lg bg-whe-bg-3 px-3 py-3">
                                  <p className="mb-1 text-xs font-medium text-whe-text-secondary">
                                    Error detail
                                  </p>
                                  {attempt.error ? (
                                    <pre className="whitespace-pre-wrap break-words font-mono text-xs text-whe-danger">
                                      {attempt.error}
                                    </pre>
                                  ) : (
                                    <p className="text-xs italic text-whe-text-muted">
                                      No error detail available.
                                    </p>
                                  )}
                                </div>
                              </td>
                            </tr>
                          )}
                        </Fragment>
                      );
                    })}
                  </tbody>
                </table>
              </div>

              {/* Pagination footer */}
              <div className="flex items-center justify-between text-sm text-whe-text-secondary">
                <span className="text-xs text-whe-text-muted">
                  Showing {state.attempts.length} of {state.pagination?.total ?? 0} attempts
                </span>
                <div className="flex items-center gap-3">
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
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
