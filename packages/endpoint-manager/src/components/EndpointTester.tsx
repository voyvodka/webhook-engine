import { useCallback, useEffect, useReducer, useRef, useState } from "react";
import type { JSX } from "react";
import type {
  PortalCapability,
  PortalEndpointSummary,
  PortalEventTypeListItem,
  PortalTestResult,
} from "../types.js";
import type { PortalClient } from "../api/createPortalClient.js";
import { PortalError } from "../api/createPortalClient.js";

export interface EndpointTesterProps {
  client: PortalClient;
  endpoint: PortalEndpointSummary;
  capabilities: PortalCapability[];
  onClose: () => void;
}

interface TesterState {
  eventTypeId: string;
  payloadText: string;
  payloadError: string | null;
  customHeaders: { key: string; value: string }[];
  submitting: boolean;
  globalError: string | null;
  result: PortalTestResult | null;
  eventTypes: PortalEventTypeListItem[];
  eventTypesLoading: boolean;
  signedRequestExpanded: boolean;
  responseBodyExpanded: boolean;
}

type TesterAction =
  | { type: "SET_EVENT_TYPE"; id: string }
  | { type: "SET_PAYLOAD"; text: string; error: string | null }
  | { type: "ADD_HEADER" }
  | { type: "UPDATE_HEADER"; index: number; key: string; value: string }
  | { type: "REMOVE_HEADER"; index: number }
  | { type: "SUBMIT_START" }
  | { type: "SUBMIT_SUCCESS"; result: PortalTestResult }
  | { type: "SUBMIT_ERROR"; message: string }
  | { type: "EVENT_TYPES_LOADED"; eventTypes: PortalEventTypeListItem[] }
  | { type: "TOGGLE_SIGNED_REQUEST" }
  | { type: "TOGGLE_RESPONSE_BODY" };

function testerReducer(state: TesterState, action: TesterAction): TesterState {
  switch (action.type) {
    case "SET_EVENT_TYPE":
      return { ...state, eventTypeId: action.id };
    case "SET_PAYLOAD":
      return { ...state, payloadText: action.text, payloadError: action.error };
    case "ADD_HEADER":
      return {
        ...state,
        customHeaders: [...state.customHeaders, { key: "", value: "" }],
      };
    case "UPDATE_HEADER": {
      const updated = state.customHeaders.map((h, i) =>
        i === action.index ? { key: action.key, value: action.value } : h,
      );
      return { ...state, customHeaders: updated };
    }
    case "REMOVE_HEADER":
      return {
        ...state,
        customHeaders: state.customHeaders.filter((_, i) => i !== action.index),
      };
    case "SUBMIT_START":
      return { ...state, submitting: true, globalError: null };
    case "SUBMIT_SUCCESS":
      return { ...state, submitting: false, result: action.result, globalError: null };
    case "SUBMIT_ERROR":
      return { ...state, submitting: false, globalError: action.message };
    case "EVENT_TYPES_LOADED":
      return { ...state, eventTypes: action.eventTypes, eventTypesLoading: false };
    case "TOGGLE_SIGNED_REQUEST":
      return { ...state, signedRequestExpanded: !state.signedRequestExpanded };
    case "TOGGLE_RESPONSE_BODY":
      return { ...state, responseBodyExpanded: !state.responseBodyExpanded };
  }
}

function headersToRecord(headers: { key: string; value: string }[]): Record<string, string> {
  const result: Record<string, string> = {};
  for (const { key, value } of headers) {
    const trimmedKey = key.trim();
    if (trimmedKey) result[trimmedKey] = value;
  }
  return result;
}

function statusColor(code: number): string {
  if (code >= 200 && code < 300) return "text-whe-success";
  if (code >= 300 && code < 400) return "text-whe-warning";
  return "text-whe-danger";
}

function statusBg(code: number): string {
  if (code >= 200 && code < 300) return "bg-whe-success-soft border-whe-success/30";
  if (code >= 300 && code < 400) return "bg-whe-warning-soft border-whe-warning/30";
  return "bg-whe-danger-soft border-whe-danger/30";
}

const SIGNATURE_HEADERS = new Set(["webhook-id", "webhook-timestamp", "webhook-signature"]);
const BODY_COLLAPSE_THRESHOLD = 500;

export function EndpointTester({
  client,
  endpoint,
  capabilities,
  onClose,
}: EndpointTesterProps): JSX.Element {
  const canTest = capabilities.includes("endpoints:test");
  const overlayRef = useRef<HTMLDivElement>(null);
  const firstFocusableRef = useRef<HTMLSelectElement | null>(null);

  const [state, dispatch] = useReducer(testerReducer, {
    eventTypeId: "",
    payloadText: "{\n  \n}",
    payloadError: null,
    customHeaders: [],
    submitting: false,
    globalError: null,
    result: null,
    eventTypes: [],
    eventTypesLoading: true,
    signedRequestExpanded: false,
    responseBodyExpanded: false,
  });

  // Track whether we've validated once (to show errors on empty submit attempt).
  const [submitted, setSubmitted] = useState(false);

  useEffect(() => {
    client.listEventTypes().then((types) => {
      dispatch({ type: "EVENT_TYPES_LOADED", eventTypes: types });
      if (types.length > 0 && types[0]) {
        dispatch({ type: "SET_EVENT_TYPE", id: types[0].id });
      }
    }).catch(() => {
      dispatch({ type: "EVENT_TYPES_LOADED", eventTypes: [] });
    });
  }, [client]);

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

  const handlePayloadBlur = useCallback(() => {
    if (!state.payloadText.trim()) return;
    try {
      JSON.parse(state.payloadText);
      dispatch({ type: "SET_PAYLOAD", text: state.payloadText, error: null });
    } catch {
      dispatch({ type: "SET_PAYLOAD", text: state.payloadText, error: "Invalid JSON" });
    }
  }, [state.payloadText]);

  const handleSubmit = useCallback(async () => {
    setSubmitted(true);

    // Validate event type.
    if (!state.eventTypeId) return;

    // Validate payload JSON.
    let parsedPayload: Record<string, unknown>;
    try {
      const parsed = JSON.parse(state.payloadText);
      if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
        dispatch({ type: "SET_PAYLOAD", text: state.payloadText, error: "Payload must be a JSON object" });
        return;
      }
      parsedPayload = parsed as Record<string, unknown>;
    } catch {
      dispatch({ type: "SET_PAYLOAD", text: state.payloadText, error: "Invalid JSON" });
      return;
    }

    dispatch({ type: "SUBMIT_START" });

    const customHeaders = headersToRecord(state.customHeaders);

    try {
      const result = await client.testEndpoint(endpoint.id, {
        eventType: state.eventTypeId,
        payload: parsedPayload,
        ...(Object.keys(customHeaders).length > 0 ? { customHeaders } : {}),
      });
      dispatch({ type: "SUBMIT_SUCCESS", result });
    } catch (err) {
      dispatch({
        type: "SUBMIT_ERROR",
        message: err instanceof PortalError
          ? err.message
          : err instanceof Error
            ? err.message
            : "An unexpected error occurred.",
      });
    }
  }, [client, endpoint.id, state.eventTypeId, state.payloadText, state.customHeaders]);

  const isPayloadInvalid = !!state.payloadError || !state.payloadText.trim();
  const isEventTypeMissing = submitted && !state.eventTypeId;
  const canSend = !state.submitting && !isPayloadInvalid && !!state.eventTypeId;

  const truncateUrl = (url: string) => url.length > 48 ? url.slice(0, 48) + "…" : url;

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
        aria-labelledby="whe-tester-title"
        className="relative flex w-full max-w-2xl flex-col overflow-hidden rounded-2xl border border-whe-border bg-whe-bg-1 shadow-2xl max-h-[90dvh]"
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-whe-border px-6 py-4">
          <div>
            <h2 id="whe-tester-title" className="text-base font-semibold text-whe-text-primary">
              Test endpoint
            </h2>
            <p className="mt-0.5 text-xs text-whe-text-muted font-mono">
              {truncateUrl(endpoint.url)}
            </p>
          </div>
          <button
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
          {!canTest ? (
            <div className="rounded-lg border border-whe-warning/30 bg-whe-warning-soft px-4 py-4 text-sm text-whe-warning">
              <p className="font-medium">Capability required</p>
              <p className="mt-1 text-xs text-whe-text-secondary">
                Your portal token does not include the <code className="rounded bg-whe-bg-3 px-1 py-0.5 font-mono text-xs">endpoints:test</code> capability.
                Contact the application operator to request access.
              </p>
            </div>
          ) : (
            <div className="flex flex-col gap-5">
              {/* Global error */}
              {state.globalError && (
                <p role="alert" className="rounded-lg bg-whe-danger-soft px-4 py-3 text-sm text-whe-danger">
                  {state.globalError}
                </p>
              )}

              {/* Two-pane: form left, result right (stacked on mobile) */}
              <div className={state.result ? "grid gap-5 lg:grid-cols-2" : "flex flex-col gap-5"}>
                {/* Request form */}
                <div className="flex flex-col gap-4">
                  {/* Event type */}
                  <div className="flex flex-col gap-1.5">
                    <label htmlFor="whe-tester-event" className="text-xs font-medium text-whe-text-secondary">
                      Event type <span className="ml-0.5 text-whe-danger" aria-hidden="true">*</span>
                    </label>
                    {state.eventTypesLoading ? (
                      <div className="h-9 animate-pulse rounded-lg bg-whe-bg-3" />
                    ) : (
                      <select
                        ref={firstFocusableRef}
                        id="whe-tester-event"
                        value={state.eventTypeId}
                        onChange={(e) => dispatch({ type: "SET_EVENT_TYPE", id: e.target.value })}
                        className={[
                          "w-full rounded-lg border bg-whe-bg-3 px-3 py-2 text-sm text-whe-text-primary",
                          "focus:outline-none focus-visible:ring-2",
                          isEventTypeMissing
                            ? "border-whe-danger focus-visible:ring-whe-danger"
                            : "border-whe-border focus-visible:ring-whe-accent",
                        ].join(" ")}
                      >
                        <option value="">— Select event type —</option>
                        {state.eventTypes.map((et) => (
                          <option key={et.id} value={et.id}>
                            {et.name}
                          </option>
                        ))}
                      </select>
                    )}
                    {isEventTypeMissing && (
                      <p role="alert" className="text-xs text-whe-danger">Event type is required</p>
                    )}
                    {state.eventTypes.length === 0 && !state.eventTypesLoading && (
                      <p className="text-xs text-whe-text-muted">No event types defined for this application.</p>
                    )}
                  </div>

                  {/* Payload */}
                  <div className="flex flex-col gap-1.5">
                    <label htmlFor="whe-tester-payload" className="text-xs font-medium text-whe-text-secondary">
                      Payload <span className="ml-0.5 text-whe-danger" aria-hidden="true">*</span>
                    </label>
                    <textarea
                      id="whe-tester-payload"
                      value={state.payloadText}
                      onChange={(e) =>
                        dispatch({ type: "SET_PAYLOAD", text: e.target.value, error: null })
                      }
                      onBlur={handlePayloadBlur}
                      rows={6}
                      spellCheck={false}
                      className={[
                        "w-full rounded-lg border bg-whe-bg-3 px-3 py-2 font-mono text-xs text-whe-text-primary placeholder:text-whe-text-muted",
                        "resize-y focus:outline-none focus-visible:ring-2",
                        state.payloadError
                          ? "border-whe-danger focus-visible:ring-whe-danger"
                          : "border-whe-border focus-visible:ring-whe-accent",
                      ].join(" ")}
                      placeholder='{"key": "value"}'
                    />
                    {state.payloadError && (
                      <p role="alert" className="text-xs text-whe-danger">{state.payloadError}</p>
                    )}
                  </div>

                  {/* Custom headers */}
                  <div className="flex flex-col gap-1.5">
                    <span className="text-xs font-medium text-whe-text-secondary">Custom headers (optional)</span>
                    <div className="space-y-2">
                      {state.customHeaders.map((header, index) => (
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
                            placeholder="Header name"
                            className="flex-1 rounded-lg border border-whe-border bg-whe-bg-3 px-3 py-2 text-sm text-whe-text-primary placeholder:text-whe-text-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-whe-accent"
                            aria-label={`Custom header ${index + 1} name`}
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
                            placeholder="Value"
                            className="flex-1 rounded-lg border border-whe-border bg-whe-bg-3 px-3 py-2 text-sm text-whe-text-primary placeholder:text-whe-text-muted focus:outline-none focus-visible:ring-2 focus-visible:ring-whe-accent"
                            aria-label={`Custom header ${index + 1} value`}
                          />
                          <button
                            type="button"
                            onClick={() => dispatch({ type: "REMOVE_HEADER", index })}
                            className="shrink-0 rounded px-2 text-whe-text-muted hover:text-whe-danger focus:outline-none focus-visible:ring-1 focus-visible:ring-whe-danger"
                            aria-label={`Remove custom header ${index + 1}`}
                          >
                            ×
                          </button>
                        </div>
                      ))}
                      <button
                        type="button"
                        onClick={() => dispatch({ type: "ADD_HEADER" })}
                        className="text-xs text-whe-accent hover:opacity-80 focus:outline-none focus-visible:ring-1 focus-visible:ring-whe-accent"
                      >
                        + Add header
                      </button>
                    </div>
                  </div>
                </div>

                {/* Response panel — renders after a successful send */}
                {state.result && (
                  <ResponsePanel
                    result={state.result}
                    signedRequestExpanded={state.signedRequestExpanded}
                    responseBodyExpanded={state.responseBodyExpanded}
                    onToggleSignedRequest={() => dispatch({ type: "TOGGLE_SIGNED_REQUEST" })}
                    onToggleResponseBody={() => dispatch({ type: "TOGGLE_RESPONSE_BODY" })}
                  />
                )}
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-2 border-t border-whe-border px-6 py-4">
          <button
            type="button"
            onClick={onClose}
            className="rounded-md px-4 py-2 text-sm text-whe-text-secondary hover:text-whe-text-primary focus:outline-none focus-visible:ring-1 focus-visible:ring-whe-accent"
          >
            {canTest ? "Cancel" : "Close"}
          </button>
          {canTest && (
            <button
              type="button"
              disabled={!canSend}
              onClick={() => void handleSubmit()}
              className="rounded-md bg-whe-accent px-4 py-2 text-sm font-medium text-whe-bg-0 hover:opacity-90 focus:outline-none focus-visible:ring-2 focus-visible:ring-whe-accent disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {state.submitting ? "Sending…" : "Send test"}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Internal: Response panel
// ---------------------------------------------------------------------------

interface ResponsePanelProps {
  result: PortalTestResult;
  signedRequestExpanded: boolean;
  responseBodyExpanded: boolean;
  onToggleSignedRequest: () => void;
  onToggleResponseBody: () => void;
}

function ResponsePanel({
  result,
  signedRequestExpanded,
  responseBodyExpanded,
  onToggleSignedRequest,
  onToggleResponseBody,
}: ResponsePanelProps): JSX.Element {
  const bodyTooLong = result.responseBody.length > BODY_COLLAPSE_THRESHOLD;
  const displayBody = !bodyTooLong || responseBodyExpanded
    ? result.responseBody
    : result.responseBody.slice(0, BODY_COLLAPSE_THRESHOLD) + "…";

  return (
    <div className="flex flex-col gap-4 rounded-xl border border-whe-border bg-whe-bg-2 p-4">
      <p className="text-xs font-semibold uppercase tracking-wide text-whe-text-muted">Response</p>

      {/* Status + latency row */}
      <div className="flex items-center gap-3">
        <span
          className={`inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-semibold ${statusBg(result.statusCode)}`}
        >
          <span className={statusColor(result.statusCode)}>{result.statusCode}</span>
        </span>
        <span className="text-xs text-whe-text-secondary">
          {result.latencyMs} ms
        </span>
        {result.error && (
          <span className="text-xs text-whe-danger">{result.error}</span>
        )}
      </div>

      {/* Response body */}
      <div className="flex flex-col gap-1">
        <p className="text-xs font-medium text-whe-text-secondary">Body</p>
        {result.responseBody ? (
          <>
            <pre className="overflow-x-auto whitespace-pre-wrap break-words rounded-lg bg-whe-bg-3 px-3 py-2 font-mono text-xs text-whe-text-primary">
              {displayBody}
            </pre>
            {bodyTooLong && (
              <button
                type="button"
                onClick={onToggleResponseBody}
                className="self-start text-xs text-whe-accent hover:opacity-80 focus:outline-none focus-visible:ring-1 focus-visible:ring-whe-accent"
              >
                {responseBodyExpanded ? "Show less" : `Show all (${result.responseBody.length} chars)`}
              </button>
            )}
          </>
        ) : (
          <p className="text-xs italic text-whe-text-muted">no body</p>
        )}
      </div>

      {/* Signed-request preview */}
      <div className="flex flex-col gap-1">
        <button
          type="button"
          onClick={onToggleSignedRequest}
          className="flex items-center gap-1.5 text-xs font-medium text-whe-text-secondary hover:text-whe-text-primary focus:outline-none focus-visible:ring-1 focus-visible:ring-whe-accent"
          aria-expanded={signedRequestExpanded}
        >
          <svg
            width="10"
            height="10"
            viewBox="0 0 10 10"
            fill="none"
            aria-hidden="true"
            className={`transition-transform ${signedRequestExpanded ? "rotate-90" : ""}`}
          >
            <path d="M3 2l4 3-4 3" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
          Signed request preview
        </button>
        {signedRequestExpanded && (
          <div className="mt-1 flex flex-col gap-2 rounded-lg border border-whe-border bg-whe-bg-3 px-3 py-3">
            <div>
              <p className="mb-1 text-xs font-medium text-whe-text-secondary">URL</p>
              <p className="break-all font-mono text-xs text-whe-text-primary">{result.request.url}</p>
            </div>
            <div>
              <p className="mb-1 text-xs font-medium text-whe-text-secondary">Headers</p>
              <div className="space-y-0.5">
                {Object.entries(result.request.headers).map(([key, value]) => {
                  const isSignatureHeader = SIGNATURE_HEADERS.has(key.toLowerCase());
                  return (
                    <div key={key} className="flex gap-2 font-mono text-xs">
                      <span
                        className={`shrink-0 ${isSignatureHeader ? "text-whe-accent font-semibold" : "text-whe-text-muted"}`}
                      >
                        {key}:
                      </span>
                      <span className="break-all text-whe-text-primary">{value}</span>
                    </div>
                  );
                })}
              </div>
            </div>
            <div>
              <p className="mb-1 text-xs font-medium text-whe-text-secondary">Body</p>
              <pre className="overflow-x-auto whitespace-pre-wrap break-words font-mono text-xs text-whe-text-primary">
                {result.request.body}
              </pre>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
