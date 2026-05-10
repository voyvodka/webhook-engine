import { useCallback, useEffect, useReducer, useRef, useState } from "react";
import type { JSX } from "react";
import type { PortalCapability, PortalEndpointDetail, PortalEventTypeListItem } from "../types.js";
import type { PortalClient } from "../api/createPortalClient.js";
import { PortalError } from "../api/createPortalClient.js";

export type EditorMode = "create" | "edit";

export interface EndpointEditorProps {
  client: PortalClient;
  capabilities: PortalCapability[];
  mode: EditorMode;
  /** Populated when mode === "edit" */
  endpoint?: PortalEndpointDetail;
  onClose: (action: "saved" | "deleted" | "cancelled") => void;
}

interface FormValues {
  url: string;
  description: string;
  filterEventTypes: string[];
  customHeaders: { key: string; value: string }[];
  secretOverride: string;
}

interface EditorState {
  values: FormValues;
  fieldErrors: Record<string, string>;
  globalError: string | null;
  saving: boolean;
  deleting: boolean;
  toggling: boolean;
  eventTypes: PortalEventTypeListItem[];
  eventTypesLoading: boolean;
  showSecret: boolean;
  showDeleteConfirm: boolean;
}

type EditorAction =
  | { type: "SET_FIELD"; field: keyof Omit<FormValues, "filterEventTypes" | "customHeaders">; value: string }
  | { type: "TOGGLE_EVENT_TYPE"; id: string }
  | { type: "ADD_HEADER" }
  | { type: "UPDATE_HEADER"; index: number; key: string; value: string }
  | { type: "REMOVE_HEADER"; index: number }
  | { type: "TOGGLE_SHOW_SECRET" }
  | { type: "TOGGLE_DELETE_CONFIRM" }
  | { type: "SAVE_START" }
  | { type: "SAVE_SUCCESS" }
  | { type: "SAVE_ERROR"; globalError: string; fieldErrors: Record<string, string> }
  | { type: "DELETE_START" }
  | { type: "DELETE_ERROR"; message: string }
  | { type: "TOGGLE_START" }
  | { type: "TOGGLE_DONE"; isActive: boolean }
  | { type: "TOGGLE_ERROR"; message: string }
  | { type: "EVENT_TYPES_LOADED"; eventTypes: PortalEventTypeListItem[] };

function editorReducer(state: EditorState, action: EditorAction): EditorState {
  switch (action.type) {
    case "SET_FIELD":
      return {
        ...state,
        values: { ...state.values, [action.field]: action.value },
        fieldErrors: { ...state.fieldErrors, [action.field]: "" },
      };
    case "TOGGLE_EVENT_TYPE": {
      const current = state.values.filterEventTypes;
      const next = current.includes(action.id)
        ? current.filter((id) => id !== action.id)
        : [...current, action.id];
      return { ...state, values: { ...state.values, filterEventTypes: next } };
    }
    case "ADD_HEADER":
      return {
        ...state,
        values: {
          ...state.values,
          customHeaders: [...state.values.customHeaders, { key: "", value: "" }],
        },
      };
    case "UPDATE_HEADER": {
      const updated = state.values.customHeaders.map((h, i) =>
        i === action.index ? { key: action.key, value: action.value } : h,
      );
      return { ...state, values: { ...state.values, customHeaders: updated } };
    }
    case "REMOVE_HEADER":
      return {
        ...state,
        values: {
          ...state.values,
          customHeaders: state.values.customHeaders.filter((_, i) => i !== action.index),
        },
      };
    case "TOGGLE_SHOW_SECRET":
      return { ...state, showSecret: !state.showSecret };
    case "TOGGLE_DELETE_CONFIRM":
      return { ...state, showDeleteConfirm: !state.showDeleteConfirm };
    case "SAVE_START":
      return { ...state, saving: true, globalError: null, fieldErrors: {} };
    case "SAVE_SUCCESS":
      return { ...state, saving: false };
    case "SAVE_ERROR":
      return {
        ...state,
        saving: false,
        globalError: action.globalError,
        fieldErrors: action.fieldErrors,
      };
    case "DELETE_START":
      return { ...state, deleting: true, globalError: null };
    case "DELETE_ERROR":
      return { ...state, deleting: false, globalError: action.message };
    case "TOGGLE_START":
      return { ...state, toggling: true, globalError: null };
    case "TOGGLE_DONE":
      return { ...state, toggling: false };
    case "TOGGLE_ERROR":
      return { ...state, toggling: false, globalError: action.message };
    case "EVENT_TYPES_LOADED":
      return { ...state, eventTypes: action.eventTypes, eventTypesLoading: false };
  }
}

function buildInitialValues(endpoint?: PortalEndpointDetail): FormValues {
  if (!endpoint) {
    return {
      url: "",
      description: "",
      filterEventTypes: [],
      customHeaders: [],
      secretOverride: "",
    };
  }
  return {
    url: endpoint.url,
    description: endpoint.description ?? "",
    filterEventTypes: endpoint.filterEventTypes,
    customHeaders: Object.entries(endpoint.customHeaders ?? {}).map(([key, value]) => ({
      key,
      value,
    })),
    secretOverride: "",
  };
}

function validateSecretOverride(value: string): string | null {
  if (value === "") return null;
  if (!value.startsWith("whsec_")) return "Secret must start with whsec_";
  const payload = value.slice("whsec_".length);
  if (payload.length < 32) return "Secret must be at least 38 characters (whsec_ + 32)";
  if (payload.length > 128) return "Secret payload must not exceed 128 characters";
  return null;
}

function validateUrl(value: string): string | null {
  if (!value) return "URL is required";
  try {
    const parsed = new URL(value);
    if (parsed.protocol !== "https:") return "URL must use HTTPS";
  } catch {
    return "URL must be a valid URL";
  }
  return null;
}

function headersToRecord(headers: { key: string; value: string }[]): Record<string, string> {
  const result: Record<string, string> = {};
  for (const { key, value } of headers) {
    const trimmedKey = key.trim();
    if (trimmedKey) result[trimmedKey] = value;
  }
  return result;
}

export function EndpointEditor({
  client,
  capabilities,
  mode,
  endpoint,
  onClose,
}: EndpointEditorProps): JSX.Element {
  const canWrite = capabilities.includes("endpoints:write");
  const isEdit = mode === "edit";
  const overlayRef = useRef<HTMLDivElement>(null);
  const firstFocusableRef = useRef<HTMLInputElement>(null);

  const [state, dispatch] = useReducer(editorReducer, {
    values: buildInitialValues(endpoint),
    fieldErrors: {},
    globalError: null,
    saving: false,
    deleting: false,
    toggling: false,
    eventTypes: [],
    eventTypesLoading: true,
    showSecret: false,
    showDeleteConfirm: false,
  });

  // Track current isActive for the enable/disable button when in edit mode.
  const [currentIsActive, setCurrentIsActive] = useState(endpoint?.isActive ?? false);

  // Load event types on mount.
  useEffect(() => {
    client.listEventTypes().then((types) => {
      dispatch({ type: "EVENT_TYPES_LOADED", eventTypes: types });
    }).catch(() => {
      dispatch({ type: "EVENT_TYPES_LOADED", eventTypes: [] });
    });
  }, [client]);

  // Focus trap and Escape key.
  useEffect(() => {
    firstFocusableRef.current?.focus();

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onClose("cancelled");
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
    // Prevent body scroll while modal is open.
    document.body.style.overflow = "hidden";

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      document.body.style.overflow = "";
    };
  }, [onClose]);

  const handleSave = useCallback(async () => {
    // Client-side validation.
    const urlError = validateUrl(state.values.url);
    const secretError = validateSecretOverride(state.values.secretOverride);
    if (urlError || secretError) {
      dispatch({
        type: "SAVE_ERROR",
        globalError: "Please fix the errors below.",
        fieldErrors: {
          ...(urlError ? { url: urlError } : {}),
          ...(secretError ? { secretOverride: secretError } : {}),
        },
      });
      return;
    }

    dispatch({ type: "SAVE_START" });

    const payload = {
      url: state.values.url,
      description: state.values.description || null,
      filterEventTypes: state.values.filterEventTypes,
      customHeaders: headersToRecord(state.values.customHeaders),
      secretOverride: state.values.secretOverride || null,
    };

    try {
      if (isEdit && endpoint) {
        await client.updateEndpoint(endpoint.id, payload);
      } else {
        await client.createEndpoint(payload);
      }
      dispatch({ type: "SAVE_SUCCESS" });
      onClose("saved");
    } catch (err) {
      if (err instanceof PortalError) {
        dispatch({
          type: "SAVE_ERROR",
          globalError: err.message,
          fieldErrors: err.fieldErrors ?? {},
        });
      } else {
        dispatch({
          type: "SAVE_ERROR",
          globalError: "An unexpected error occurred.",
          fieldErrors: {},
        });
      }
    }
  }, [client, endpoint, isEdit, onClose, state.values]);

  const handleDelete = useCallback(async () => {
    if (!endpoint) return;
    dispatch({ type: "DELETE_START" });
    try {
      await client.deleteEndpoint(endpoint.id);
      onClose("deleted");
    } catch (err) {
      dispatch({
        type: "DELETE_ERROR",
        message: err instanceof Error ? err.message : "Failed to delete endpoint.",
      });
    }
  }, [client, endpoint, onClose]);

  const handleToggleActive = useCallback(async () => {
    if (!endpoint) return;
    dispatch({ type: "TOGGLE_START" });
    try {
      if (currentIsActive) {
        await client.disableEndpoint(endpoint.id);
        setCurrentIsActive(false);
      } else {
        await client.enableEndpoint(endpoint.id);
        setCurrentIsActive(true);
      }
      dispatch({ type: "TOGGLE_DONE", isActive: !currentIsActive });
    } catch (err) {
      dispatch({
        type: "TOGGLE_ERROR",
        message: err instanceof Error ? err.message : "Failed to update endpoint status.",
      });
    }
  }, [client, currentIsActive, endpoint]);

  const title = isEdit ? "Edit endpoint" : "New endpoint";

  return (
    /* Backdrop */
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose("cancelled");
      }}
      aria-hidden="false"
    >
      {/* Dialog */}
      <div
        ref={overlayRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="whe-editor-title"
        className="relative flex w-full max-w-lg flex-col overflow-hidden rounded-2xl border border-whe-border bg-whe-bg-1 shadow-2xl max-h-[90dvh]"
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-whe-border px-6 py-4">
          <h2 id="whe-editor-title" className="text-base font-semibold text-whe-text-primary">
            {title}
          </h2>
          <button
            type="button"
            onClick={() => onClose("cancelled")}
            className="rounded-md p-1 text-whe-text-muted hover:text-whe-text-primary focus:outline-none focus-visible:ring-2 focus-visible:ring-whe-accent"
            aria-label="Close"
          >
            {/* X icon via CSS — no Lucide dep inside this package */}
            <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true">
              <path d="M12 4L4 12M4 4l8 8" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
            </svg>
          </button>
        </div>

        {/* Body — scrollable */}
        <div className="overflow-y-auto px-6 py-5 space-y-5">
          {/* Global error */}
          {state.globalError && (
            <p
              role="alert"
              className="rounded-lg bg-whe-danger-soft px-4 py-3 text-sm text-whe-danger"
            >
              {state.globalError}
            </p>
          )}

          {/* URL */}
          <Field
            label="Endpoint URL"
            htmlFor="whe-url"
            error={state.fieldErrors["url"]}
            required
          >
            <input
              ref={firstFocusableRef}
              id="whe-url"
              type="url"
              value={state.values.url}
              onChange={(e) => dispatch({ type: "SET_FIELD", field: "url", value: e.target.value })}
              disabled={!canWrite}
              placeholder="https://your-app.com/webhooks"
              className={inputClass(!!state.fieldErrors["url"], !canWrite)}
              autoComplete="off"
            />
          </Field>

          {/* Description */}
          <Field label="Description" htmlFor="whe-desc" error={state.fieldErrors["description"]}>
            <input
              id="whe-desc"
              type="text"
              value={state.values.description}
              onChange={(e) =>
                dispatch({ type: "SET_FIELD", field: "description", value: e.target.value })
              }
              disabled={!canWrite}
              placeholder="Optional — shown in the endpoint list"
              className={inputClass(!!state.fieldErrors["description"], !canWrite)}
            />
          </Field>

          {/* Filter event types */}
          <Field label="Filter event types" htmlFor="whe-event-types">
            {state.eventTypesLoading ? (
              <div className="h-24 animate-pulse rounded-lg bg-whe-bg-3" />
            ) : state.eventTypes.length === 0 ? (
              <p className="text-xs text-whe-text-muted">No event types defined for this application.</p>
            ) : (
              <div className="flex flex-wrap gap-2">
                {state.eventTypes.map((et) => {
                  const selected = state.values.filterEventTypes.includes(et.id);
                  return (
                    <button
                      key={et.id}
                      type="button"
                      disabled={!canWrite}
                      onClick={() => dispatch({ type: "TOGGLE_EVENT_TYPE", id: et.id })}
                      className={[
                        "rounded-full border px-3 py-1 text-xs font-medium transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-whe-accent",
                        selected
                          ? "border-whe-accent bg-whe-accent-soft text-whe-accent"
                          : "border-whe-border bg-whe-bg-3 text-whe-text-secondary hover:border-whe-accent hover:text-whe-accent",
                        !canWrite ? "cursor-not-allowed opacity-60" : "",
                      ]
                        .filter(Boolean)
                        .join(" ")}
                      aria-pressed={selected}
                    >
                      {et.name}
                    </button>
                  );
                })}
              </div>
            )}
            {state.values.filterEventTypes.length === 0 && (
              <p className="mt-1 text-xs text-whe-text-muted">
                No filters selected — all event types will be delivered.
              </p>
            )}
          </Field>

          {/* Custom headers */}
          <Field label="Custom headers" htmlFor="whe-headers">
            <div className="space-y-2">
              {state.values.customHeaders.map((header, index) => (
                <div key={index} className="flex gap-2">
                  <input
                    type="text"
                    value={header.key}
                    onChange={(e) =>
                      dispatch({
                        type: "UPDATE_HEADER",
                        index,
                        key: e.target.value,
                        value: header.value,
                      })
                    }
                    disabled={!canWrite}
                    placeholder="Header name"
                    className={`${inputClass(false, !canWrite)} flex-1`}
                    aria-label={`Header ${index + 1} name`}
                  />
                  <input
                    type="text"
                    value={header.value}
                    onChange={(e) =>
                      dispatch({
                        type: "UPDATE_HEADER",
                        index,
                        key: header.key,
                        value: e.target.value,
                      })
                    }
                    disabled={!canWrite}
                    placeholder="Value"
                    className={`${inputClass(false, !canWrite)} flex-1`}
                    aria-label={`Header ${index + 1} value`}
                  />
                  {canWrite && (
                    <button
                      type="button"
                      onClick={() => dispatch({ type: "REMOVE_HEADER", index })}
                      className="shrink-0 rounded px-2 text-whe-text-muted hover:text-whe-danger focus:outline-none focus-visible:ring-1 focus-visible:ring-whe-danger"
                      aria-label={`Remove header ${index + 1}`}
                    >
                      ×
                    </button>
                  )}
                </div>
              ))}
              {canWrite && (
                <button
                  type="button"
                  onClick={() => dispatch({ type: "ADD_HEADER" })}
                  className="text-xs text-whe-accent hover:opacity-80 focus:outline-none focus-visible:ring-1 focus-visible:ring-whe-accent"
                >
                  + Add header
                </button>
              )}
            </div>
            {state.fieldErrors["customHeaders"] && (
              <p className="mt-1 text-xs text-whe-danger">{state.fieldErrors["customHeaders"]}</p>
            )}
          </Field>

          {/* Secret override (edit mode only) */}
          {isEdit && (
            <Field
              label="Secret override"
              htmlFor="whe-secret"
              error={state.fieldErrors["secretOverride"]}
              hint="Leave blank to keep the current signing secret. To rotate to a fully new secret, use the key-rotation flow from the operator dashboard instead."
            >
              <div className="relative">
                <input
                  id="whe-secret"
                  type={state.showSecret ? "text" : "password"}
                  value={state.values.secretOverride}
                  onChange={(e) =>
                    dispatch({
                      type: "SET_FIELD",
                      field: "secretOverride",
                      value: e.target.value,
                    })
                  }
                  disabled={!canWrite}
                  placeholder="whsec_…"
                  className={`${inputClass(!!state.fieldErrors["secretOverride"], !canWrite)} pr-16`}
                  autoComplete="new-password"
                />
                <button
                  type="button"
                  onClick={() => dispatch({ type: "TOGGLE_SHOW_SECRET" })}
                  className="absolute right-2 top-1/2 -translate-y-1/2 rounded px-2 py-0.5 text-xs text-whe-text-muted hover:text-whe-text-primary focus:outline-none focus-visible:ring-1 focus-visible:ring-whe-accent"
                  aria-label={state.showSecret ? "Hide secret" : "Show secret"}
                >
                  {state.showSecret ? "Hide" : "Show"}
                </button>
              </div>
            </Field>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between border-t border-whe-border px-6 py-4">
          {/* Destructive actions (edit mode) */}
          {isEdit && canWrite && (
            <div className="flex items-center gap-2">
              <button
                type="button"
                disabled={state.toggling || state.saving || state.deleting}
                onClick={() => void handleToggleActive()}
                className="rounded-md border border-whe-border px-3 py-1.5 text-sm text-whe-text-secondary hover:border-whe-accent hover:text-whe-accent focus:outline-none focus-visible:ring-2 focus-visible:ring-whe-accent disabled:opacity-50"
              >
                {state.toggling ? "…" : currentIsActive ? "Disable" : "Enable"}
              </button>

              {!state.showDeleteConfirm ? (
                <button
                  type="button"
                  disabled={state.toggling || state.saving || state.deleting}
                  onClick={() => dispatch({ type: "TOGGLE_DELETE_CONFIRM" })}
                  className="rounded-md border border-whe-danger/30 px-3 py-1.5 text-sm text-whe-danger hover:border-whe-danger focus:outline-none focus-visible:ring-2 focus-visible:ring-whe-danger disabled:opacity-50"
                >
                  Delete
                </button>
              ) : (
                <span className="flex items-center gap-2">
                  <span className="text-xs text-whe-text-secondary">Confirm?</span>
                  <button
                    type="button"
                    disabled={state.deleting}
                    onClick={() => void handleDelete()}
                    className="rounded-md bg-whe-danger px-3 py-1.5 text-sm font-medium text-white hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-whe-danger disabled:opacity-50"
                  >
                    {state.deleting ? "Deleting…" : "Yes, delete"}
                  </button>
                  <button
                    type="button"
                    onClick={() => dispatch({ type: "TOGGLE_DELETE_CONFIRM" })}
                    className="rounded-md px-3 py-1.5 text-sm text-whe-text-secondary hover:text-whe-text-primary focus:outline-none focus-visible:ring-1 focus-visible:ring-whe-accent"
                  >
                    Cancel
                  </button>
                </span>
              )}
            </div>
          )}

          {/* Right side — close + save */}
          <div className={`flex items-center gap-2 ${isEdit && canWrite ? "" : "ml-auto"}`}>
            <button
              type="button"
              onClick={() => onClose("cancelled")}
              className="rounded-md px-4 py-2 text-sm text-whe-text-secondary hover:text-whe-text-primary focus:outline-none focus-visible:ring-1 focus-visible:ring-whe-accent"
            >
              Cancel
            </button>
            {canWrite && (
              <button
                type="button"
                disabled={state.saving || state.deleting || state.toggling}
                onClick={() => void handleSave()}
                className="rounded-md bg-whe-accent px-4 py-2 text-sm font-medium text-whe-bg-0 hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-whe-accent disabled:opacity-50"
              >
                {state.saving ? "Saving…" : "Save"}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

// Internal helper components.

interface FieldProps {
  label: string;
  htmlFor: string;
  error?: string;
  hint?: string;
  required?: boolean;
  children: React.ReactNode;
}

function Field({ label, htmlFor, error, hint, required, children }: FieldProps): JSX.Element {
  return (
    <div className="flex flex-col gap-1.5">
      <label
        htmlFor={htmlFor}
        className="text-xs font-medium text-whe-text-secondary"
      >
        {label}
        {required && <span className="ml-0.5 text-whe-danger" aria-hidden="true">*</span>}
      </label>
      {children}
      {hint && <p className="text-xs text-whe-text-muted">{hint}</p>}
      {error && (
        <p role="alert" className="text-xs text-whe-danger">
          {error}
        </p>
      )}
    </div>
  );
}

function inputClass(hasError: boolean, disabled: boolean): string {
  return [
    "w-full rounded-lg border bg-whe-bg-3 px-3 py-2 text-sm text-whe-text-primary placeholder:text-whe-text-muted",
    "focus:outline-none focus-visible:ring-2",
    hasError
      ? "border-whe-danger focus-visible:ring-whe-danger"
      : "border-whe-border focus-visible:ring-whe-accent",
    disabled ? "cursor-not-allowed opacity-60" : "",
  ]
    .filter(Boolean)
    .join(" ");
}
