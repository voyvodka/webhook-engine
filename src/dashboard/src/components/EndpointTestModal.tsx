import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { json } from "@codemirror/lang-json";
import { EditorView } from "@codemirror/view";
import { tags as t } from "@lezer/highlight";
import { HighlightStyle, syntaxHighlighting } from "@codemirror/language";
import { AlertCircle, CheckCircle2, ChevronDown, ChevronUp, Loader2, Send } from "lucide-react";
import { Modal } from "./Modal";
import { testDashboardEndpoint } from "../api/dashboardApi";
import type { TestEndpointResult } from "../api/dashboardApi";

interface EndpointTestModalProps {
  open: boolean;
  endpointId: string | null;
  endpointUrl: string | null;
  onClose: () => void;
}

const DEFAULT_PAYLOAD = `{
  "id": "evt_test_001",
  "data": {
    "message": "Hello from WebhookEngine"
  }
}`;

const editorTheme = EditorView.theme(
  {
    "&": {
      backgroundColor: "transparent",
      color: "#fafafa",
      fontSize: "12px",
      fontFamily:
        "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace"
    },
    ".cm-content": { padding: "8px 0", caretColor: "#22d3ee" },
    ".cm-gutters": {
      backgroundColor: "transparent",
      color: "#71717a",
      borderRight: "1px solid #1e1e22"
    },
    ".cm-activeLine": { backgroundColor: "transparent" },
    ".cm-activeLineGutter": { backgroundColor: "transparent" },
    ".cm-cursor": { borderLeftColor: "#22d3ee" },
    "&.cm-focused": { outline: "none" },
    "&.cm-focused .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection":
      { backgroundColor: "rgba(34, 211, 238, 0.18)" }
  },
  { dark: true }
);

const editorHighlight = HighlightStyle.define([
  { tag: t.string, color: "#86efac" },
  { tag: t.number, color: "#fbbf24" },
  { tag: t.bool, color: "#f472b6" },
  { tag: t.null, color: "#71717a" },
  { tag: t.propertyName, color: "#22d3ee" },
  { tag: t.keyword, color: "#c4b5fd" },
  { tag: t.punctuation, color: "#a1a1aa" }
]);

const editorExtensions = [editorTheme, syntaxHighlighting(editorHighlight), json(), EditorView.lineWrapping];

const inputClasses =
  "w-full px-3 py-2 text-sm bg-surface-2 border border-border rounded-lg text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-1 focus:ring-accent/50 focus:border-accent/50 transition-colors";

export function EndpointTestModal({ open, endpointId, endpointUrl, onClose }: EndpointTestModalProps) {
  const [eventType, setEventType] = useState("");
  const [payloadText, setPayloadText] = useState(DEFAULT_PAYLOAD);
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<TestEndpointResult | null>(null);
  const [error, setError] = useState("");
  const [showPreview, setShowPreview] = useState(false);
  const isMountedRef = useRef(true);

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
    };
  }, []);

  // Inline JSON parse — used both for the disabled state on the Send button
  // and for the actual request body. Empty string is treated as "no payload"
  // so the backend's default-payload fallback can run.
  const parsedPayload = useMemo<{ ok: true; value: unknown | undefined } | { ok: false; error: string }>(() => {
    const trimmed = payloadText.trim();
    if (!trimmed) return { ok: true, value: undefined };
    try {
      return { ok: true, value: JSON.parse(trimmed) };
    } catch (e) {
      return { ok: false, error: e instanceof Error ? e.message : "Invalid JSON" };
    }
  }, [payloadText]);

  const handleClose = useCallback(() => {
    if (submitting) return;
    onClose();
    // Reset on close so reopening starts fresh
    setEventType("");
    setPayloadText(DEFAULT_PAYLOAD);
    setResult(null);
    setError("");
    setShowPreview(false);
  }, [submitting, onClose]);

  const handleSend = useCallback(async () => {
    if (!endpointId || !parsedPayload.ok || submitting) return;
    setSubmitting(true);
    setError("");
    setResult(null);
    try {
      const probeResult = await testDashboardEndpoint(endpointId, {
        eventType: eventType.trim() || undefined,
        payload: parsedPayload.value
      });
      if (!isMountedRef.current) return;
      setResult(probeResult);
    } catch (e) {
      if (!isMountedRef.current) return;
      setError(e instanceof Error ? e.message : "Test request failed");
    } finally {
      if (isMountedRef.current) setSubmitting(false);
    }
  }, [endpointId, eventType, parsedPayload, submitting]);

  if (!endpointId) return null;

  const sendDisabled = submitting || !parsedPayload.ok;

  return (
    <Modal
      open={open}
      onClose={handleClose}
      title="Send test webhook"
      description={endpointUrl ?? undefined}
      width="max-w-2xl"
    >
      <div className="space-y-4">
        {/* Event type */}
        <div>
          <label className="block text-xs font-medium text-text-secondary mb-1.5">Event type</label>
          <input
            type="text"
            value={eventType}
            onChange={(e) => setEventType(e.target.value)}
            placeholder="optional, e.g. order.created"
            className={inputClasses}
            disabled={submitting}
            maxLength={256}
          />
          <p className="text-[11px] text-text-muted mt-1">
            Sent as the event-type identifier in the default payload. Leave empty to use the bare default body.
          </p>
        </div>

        {/* Payload editor */}
        <div>
          <label className="block text-xs font-medium text-text-secondary mb-1.5">Payload (JSON)</label>
          <div className="rounded-lg border border-border bg-surface-2 overflow-hidden">
            <CodeMirror
              value={payloadText}
              height="200px"
              extensions={editorExtensions}
              onChange={setPayloadText}
              basicSetup={{
                lineNumbers: false,
                foldGutter: false,
                highlightActiveLine: false
              }}
            />
          </div>
          {!parsedPayload.ok && (
            <p className="text-[11px] text-danger mt-1.5 font-mono">JSON parse error: {parsedPayload.error}</p>
          )}
        </div>

        {/* Action row */}
        <div className="flex items-center justify-end gap-2 pt-1">
          <button
            type="button"
            onClick={handleClose}
            disabled={submitting}
            className="text-xs font-medium px-3 py-1.5 rounded-lg text-text-secondary hover:text-text-primary hover:bg-surface-2 disabled:opacity-50 transition-colors"
          >
            Close
          </button>
          <button
            type="button"
            onClick={handleSend}
            disabled={sendDisabled}
            className="inline-flex items-center gap-1.5 text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {submitting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Send className="w-3.5 h-3.5" />}
            {submitting ? "Sending…" : "Send"}
          </button>
        </div>

        {/* Error from network or 4xx/5xx envelope */}
        {error && (
          <div className="flex items-start gap-2 text-danger text-xs bg-danger-soft border border-danger/20 rounded-lg px-3 py-2">
            <AlertCircle className="w-3.5 h-3.5 shrink-0 mt-0.5" />
            <span className="font-mono break-all">{error}</span>
          </div>
        )}

        {/* Result panel */}
        {result && (
          <div className="rounded-lg border border-border-subtle bg-surface-2/40 overflow-hidden">
            <div
              className={`flex items-center gap-2 px-3 py-2 text-xs ${result.success ? "text-success" : "text-danger"} border-b border-border-subtle`}
            >
              {result.success ? <CheckCircle2 className="w-3.5 h-3.5" /> : <AlertCircle className="w-3.5 h-3.5" />}
              <span className="font-mono">
                {result.success
                  ? `Delivered → HTTP ${result.statusCode} in ${result.latencyMs}ms`
                  : `Failed → HTTP ${result.statusCode || "—"} ${result.error ?? ""}`.trim()}
              </span>
            </div>

            {result.responseBody && (
              <div className="px-3 py-2 border-b border-border-subtle">
                <p className="text-[11px] font-medium text-text-secondary mb-1">Response body</p>
                <pre className="text-[11px] font-mono text-text-primary bg-surface-3/40 rounded-md p-2 max-h-40 overflow-auto whitespace-pre-wrap break-all">
                  {result.responseBody}
                </pre>
              </div>
            )}

            <button
              type="button"
              onClick={() => setShowPreview((s) => !s)}
              className="w-full flex items-center justify-between gap-2 px-3 py-2 text-xs text-text-secondary hover:text-text-primary hover:bg-surface-3/40 transition-colors"
            >
              <span className="font-medium">Signed request preview</span>
              {showPreview ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
            </button>

            {showPreview && (
              <div className="px-3 pb-3 space-y-2">
                <div>
                  <p className="text-[11px] font-medium text-text-secondary mb-1">URL</p>
                  <p className="text-[11px] font-mono text-text-primary break-all bg-surface-3/40 rounded-md px-2 py-1.5">
                    POST {result.request.url}
                  </p>
                </div>
                <div>
                  <p className="text-[11px] font-medium text-text-secondary mb-1">Headers</p>
                  <div className="text-[11px] font-mono bg-surface-3/40 rounded-md p-2 space-y-0.5">
                    {Object.entries(result.request.headers).map(([k, v]) => (
                      <div key={k} className="break-all">
                        <span className="text-accent">{k}:</span>{" "}
                        <span className="text-text-primary">{v}</span>
                      </div>
                    ))}
                  </div>
                </div>
                <div>
                  <p className="text-[11px] font-medium text-text-secondary mb-1">Body</p>
                  <pre className="text-[11px] font-mono text-text-primary bg-surface-3/40 rounded-md p-2 max-h-40 overflow-auto whitespace-pre-wrap break-all">
                    {result.request.body}
                  </pre>
                </div>
              </div>
            )}
          </div>
        )}
      </div>
    </Modal>
  );
}
