import { useState, useCallback, useMemo } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { json } from "@codemirror/lang-json";
import { EditorView } from "@codemirror/view";
import { tags as t } from "@lezer/highlight";
import { HighlightStyle, syntaxHighlighting } from "@codemirror/language";
import { validateTransform } from "../api/dashboardApi";
import { Play, Loader2, CheckCircle2, AlertCircle, Wand2, ChevronDown, ChevronUp } from "lucide-react";

interface TransformSectionProps {
  enabled: boolean;
  expression: string;
  onChange: (next: { enabled: boolean; expression: string }) => void;
  /** Optional sample payload prefilled by the parent (e.g. from a recent message). */
  initialSamplePayload?: string;
}

const DEFAULT_SAMPLE = `{
  "user": {
    "id": "u_123",
    "email": "alice@example.com"
  },
  "amount": 4200
}`;

// CodeMirror dark theme tuned to the dashboard's design tokens.
// Surface and border match Tailwind's bg-surface-2 / border-border so the
// editor blends into the modal instead of looking like a foreign widget.
const dashboardEditorTheme = EditorView.theme(
  {
    "&": {
      backgroundColor: "transparent",
      color: "#fafafa",
      fontSize: "12px",
      fontFamily:
        "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace"
    },
    ".cm-content": {
      padding: "8px 0",
      caretColor: "#22d3ee"
    },
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

const dashboardHighlight = HighlightStyle.define([
  { tag: t.string, color: "#86efac" },
  { tag: t.number, color: "#fbbf24" },
  { tag: t.bool, color: "#f472b6" },
  { tag: t.null, color: "#71717a" },
  { tag: t.propertyName, color: "#22d3ee" },
  { tag: t.keyword, color: "#c4b5fd" },
  { tag: t.punctuation, color: "#a1a1aa" }
]);

const expressionExtensions = [
  dashboardEditorTheme,
  EditorView.lineWrapping
];

const payloadExtensions = [
  dashboardEditorTheme,
  syntaxHighlighting(dashboardHighlight),
  json(),
  EditorView.lineWrapping
];

export function TransformSection({
  enabled,
  expression,
  onChange,
  initialSamplePayload
}: TransformSectionProps) {
  const [showPlayground, setShowPlayground] = useState(false);
  const [samplePayload, setSamplePayload] = useState(initialSamplePayload || DEFAULT_SAMPLE);
  const [running, setRunning] = useState(false);
  const [result, setResult] = useState<{ success: boolean; output: string; error: string | null } | null>(null);

  const handleEnabledChange = (next: boolean) => {
    onChange({ enabled: next, expression });
  };

  const handleExpressionChange = useCallback(
    (next: string) => {
      onChange({ enabled, expression: next });
      setResult(null);
    },
    [enabled, onChange]
  );

  const handleSampleChange = useCallback((next: string) => {
    setSamplePayload(next);
    setResult(null);
  }, []);

  const canRun = useMemo(
    () => expression.trim().length > 0 && samplePayload.trim().length > 0 && !running,
    [expression, samplePayload, running]
  );

  const handleRun = async () => {
    if (!canRun) return;
    setRunning(true);
    setResult(null);
    try {
      const response = await validateTransform({
        expression: expression.trim(),
        samplePayload
      });
      setResult({
        success: response.success,
        output: response.transformed ?? "",
        error: response.error
      });
    } catch (e) {
      setResult({
        success: false,
        output: "",
        error: e instanceof Error ? e.message : "Failed to validate expression"
      });
    } finally {
      setRunning(false);
    }
  };

  return (
    <div className="rounded-lg border border-border-subtle bg-surface-1">
      {/* Header — toggle */}
      <div className="flex items-center justify-between px-3 py-2.5">
        <div className="flex items-center gap-2 min-w-0">
          <Wand2 className="w-3.5 h-3.5 text-text-muted shrink-0" />
          <div className="min-w-0">
            <p className="text-xs font-medium text-text-secondary">Payload transformation</p>
            <p className="text-[11px] text-text-muted truncate">
              Reshape the body with a JMESPath expression before delivery.
            </p>
          </div>
        </div>
        <label className="inline-flex items-center gap-2 cursor-pointer shrink-0">
          <span className="text-[11px] text-text-muted">{enabled ? "Enabled" : "Disabled"}</span>
          <input
            type="checkbox"
            checked={enabled}
            onChange={(e) => handleEnabledChange(e.target.checked)}
            className="sr-only peer"
          />
          <span className="relative w-7 h-4 rounded-full bg-surface-3 peer-checked:bg-accent transition-colors">
            <span className="absolute top-0.5 left-0.5 w-3 h-3 rounded-full bg-text-primary peer-[]:transition-transform peer-checked:translate-x-3" />
          </span>
        </label>
      </div>

      {/* Expression editor */}
      <div className="border-t border-border-subtle">
        <div className="px-3 pt-2 pb-1 flex items-center justify-between">
          <span className="text-[11px] font-medium text-text-muted">JMESPath expression</span>
          <a
            href="https://jmespath.org/tutorial.html"
            target="_blank"
            rel="noreferrer"
            className="text-[11px] text-text-muted hover:text-accent"
          >
            JMESPath docs ↗
          </a>
        </div>
        <div className="px-3 pb-2">
          <div className="rounded-md border border-border bg-surface-2 px-2 py-1">
            <CodeMirror
              value={expression}
              onChange={handleExpressionChange}
              extensions={expressionExtensions}
              basicSetup={{
                lineNumbers: false,
                foldGutter: false,
                highlightActiveLine: false,
                highlightActiveLineGutter: false,
                highlightSelectionMatches: false,
                autocompletion: false,
                dropCursor: false
              }}
              placeholder='{ id: user.id, email: user.email }'
              minHeight="32px"
              maxHeight="120px"
            />
          </div>
        </div>
      </div>

      {/* Playground toggle + body */}
      <div className="border-t border-border-subtle">
        <button
          type="button"
          onClick={() => setShowPlayground((s) => !s)}
          className="w-full flex items-center justify-between px-3 py-2 text-[11px] font-medium text-text-secondary hover:bg-surface-2 transition-colors"
        >
          <span className="flex items-center gap-1.5">
            {showPlayground ? <ChevronUp className="w-3 h-3" /> : <ChevronDown className="w-3 h-3" />}
            Test with sample payload
          </span>
          <span className="text-text-muted">
            {result === null ? "" : result.success ? "Last run: ok" : "Last run: failed"}
          </span>
        </button>

        {showPlayground && (
          <div className="px-3 pb-3 space-y-2">
            <div>
              <span className="text-[11px] font-medium text-text-muted block mb-1">Sample payload (JSON)</span>
              <div className="rounded-md border border-border bg-surface-2 px-2 py-1">
                <CodeMirror
                  value={samplePayload}
                  onChange={handleSampleChange}
                  extensions={payloadExtensions}
                  basicSetup={{
                    lineNumbers: true,
                    foldGutter: false,
                    highlightActiveLine: false,
                    highlightActiveLineGutter: false,
                    highlightSelectionMatches: false,
                    autocompletion: false
                  }}
                  minHeight="80px"
                  maxHeight="180px"
                />
              </div>
            </div>

            <div className="flex items-center justify-end">
              <button
                type="button"
                onClick={handleRun}
                disabled={!canRun}
                className="inline-flex items-center gap-1.5 text-[11px] font-medium px-2.5 py-1.5 rounded-md bg-accent text-zinc-950 hover:bg-accent/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                {running ? <Loader2 className="w-3 h-3 animate-spin" /> : <Play className="w-3 h-3" />}
                {running ? "Running..." : "Run"}
              </button>
            </div>

            {result && (
              <div>
                <div
                  className={`flex items-center gap-1.5 text-[11px] mb-1 ${
                    result.success ? "text-success" : "text-danger"
                  }`}
                >
                  {result.success ? (
                    <CheckCircle2 className="w-3 h-3" />
                  ) : (
                    <AlertCircle className="w-3 h-3" />
                  )}
                  <span className="font-medium">
                    {result.success ? "Transformed output" : "Expression rejected"}
                  </span>
                </div>
                {result.success ? (
                  <div className="rounded-md border border-border bg-surface-2 px-2 py-1">
                    <CodeMirror
                      value={result.output}
                      editable={false}
                      extensions={payloadExtensions}
                      basicSetup={{
                        lineNumbers: true,
                        foldGutter: false,
                        highlightActiveLine: false,
                        highlightActiveLineGutter: false,
                        highlightSelectionMatches: false,
                        autocompletion: false
                      }}
                      minHeight="60px"
                      maxHeight="200px"
                    />
                  </div>
                ) : (
                  <div className="rounded-md border border-danger/20 bg-danger-soft text-danger text-[11px] px-2 py-1.5 font-mono">
                    {result.error ?? "Unknown error"}
                  </div>
                )}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
