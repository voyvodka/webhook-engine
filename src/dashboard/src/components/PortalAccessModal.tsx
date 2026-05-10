import { useState, useRef, useCallback } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Globe,
  Copy,
  Check,
  AlertCircle,
  X,
  Plus,
  CheckCircle2
} from "lucide-react";
import { Modal } from "./Modal";
import { ConfirmModal } from "./ConfirmModal";
import {
  getPortalAccess,
  enablePortal,
  rotatePortalKey,
  disablePortal,
  updatePortalOrigins,
  ApiErrorException
} from "../api/dashboardApi";
import type { PortalSigningKeyReveal } from "../api/dashboardApi";
import { formatLocaleDateTime } from "../utils/dateTime";

// ── Copy button (same pattern as ApplicationsPage) ───────────────────────────

function CopyButton({ text, className }: { text: string; className?: string }) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    await navigator.clipboard.writeText(text);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <button
      onClick={handleCopy}
      className={className ?? "p-1 rounded hover:bg-surface-3 text-text-muted hover:text-text-primary transition-colors"}
      title="Copy"
    >
      {copied ? <Check className="w-3.5 h-3.5 text-success" /> : <Copy className="w-3.5 h-3.5" />}
    </button>
  );
}

// ── Embed snippet code blocks ─────────────────────────────────────────────────

const MINT_SNIPPET = `// In your backend (Express, Next API route, etc.)
import { SignJWT } from "jose";

export async function mintPortalToken(appId: string) {
  const secret = new TextEncoder().encode(process.env.PORTAL_SIGNING_KEY);
  return await new SignJWT({
    appId,
    capabilities: ["endpoints:read", "endpoints:write", "endpoints:test", "attempts:read"]
  })
    .setProtectedHeader({ alg: "HS256" })
    .setIssuedAt()
    .setNotBefore("0s")
    .setExpirationTime("10m")
    .sign(secret);
}`;

const RENDER_SNIPPET = `// In your settings page
import { EndpointManager } from "@webhookengine/endpoint-manager";

<EndpointManager
  baseUrl="https://hooks.your-domain.com"
  appId="<the same appId>"
  token={await mintPortalToken("<the same appId>")}
/>`;

function CodeBlock({ code, label }: { code: string; label: string }) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    await navigator.clipboard.writeText(code);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="space-y-1">
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium text-text-secondary">{label}</span>
        <button
          onClick={handleCopy}
          className="inline-flex items-center gap-1 text-xs text-text-muted hover:text-text-primary transition-colors"
          title="Copy snippet"
        >
          {copied ? (
            <><Check className="w-3 h-3 text-success" /><span className="text-success">Copied</span></>
          ) : (
            <><Copy className="w-3 h-3" />Copy</>
          )}
        </button>
      </div>
      <pre className="bg-surface-0 border border-border rounded-lg px-3 py-2.5 text-xs font-mono text-text-secondary overflow-x-auto leading-relaxed whitespace-pre">
        <code>{code}</code>
      </pre>
    </div>
  );
}

// ── Origin chip list ──────────────────────────────────────────────────────────

function validateOrigin(value: string): string | null {
  const trimmed = value.trim();
  if (!trimmed.startsWith("http://") && !trimmed.startsWith("https://")) {
    return "Origin must start with http:// or https://";
  }
  try {
    const url = new URL(trimmed);
    if (url.pathname !== "/" && url.pathname !== "") {
      return "Origin must not include a path";
    }
    if (url.search || url.hash) {
      return "Origin must not include query string or fragment";
    }
  } catch {
    return "Invalid URL";
  }
  if (trimmed.includes("*")) {
    return "Wildcards are not allowed";
  }
  return null;
}

interface OriginChipListProps {
  origins: string[];
  onChange: (origins: string[]) => void;
  disabled: boolean;
}

function OriginChipList({ origins, onChange, disabled }: OriginChipListProps) {
  const [inputValue, setInputValue] = useState("");
  const [inputError, setInputError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const addOrigin = useCallback(() => {
    const trimmed = inputValue.trim();
    if (!trimmed) return;

    const err = validateOrigin(trimmed);
    if (err) {
      setInputError(err);
      return;
    }

    // Normalize: lowercase scheme + host, strip trailing slash
    let normalized = trimmed.toLowerCase();
    try {
      const url = new URL(normalized);
      normalized = `${url.protocol}//${url.host}`;
    } catch {
      // keep as-is — backend will canonicalize / reject
    }

    if (origins.includes(normalized)) {
      setInputError("Origin already in the list");
      return;
    }

    onChange([...origins, normalized]);
    setInputValue("");
    setInputError(null);
    inputRef.current?.focus();
  }, [inputValue, origins, onChange]);

  const removeOrigin = (origin: string) => {
    onChange(origins.filter((o) => o !== origin));
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") {
      e.preventDefault();
      addOrigin();
    }
  };

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-1.5">
        {origins.map((origin) => (
          <span
            key={origin}
            className="inline-flex items-center gap-1 text-xs font-mono bg-surface-2 border border-border rounded-md px-2 py-0.5 text-text-secondary"
          >
            {origin}
            {!disabled && (
              <button
                type="button"
                onClick={() => removeOrigin(origin)}
                className="text-text-muted hover:text-danger transition-colors ml-0.5"
                aria-label={`Remove ${origin}`}
              >
                <X className="w-3 h-3" />
              </button>
            )}
          </span>
        ))}
        {origins.length === 0 && (
          <span className="text-xs text-text-muted italic">No origins configured</span>
        )}
      </div>

      {!disabled && (
        <div className="flex items-start gap-2">
          <div className="flex-1">
            <input
              ref={inputRef}
              type="url"
              value={inputValue}
              onChange={(e) => { setInputValue(e.target.value); setInputError(null); }}
              onKeyDown={handleKeyDown}
              placeholder="https://app.example.com"
              className="w-full px-3 py-1.5 text-xs font-mono bg-surface-2 border border-border rounded-lg text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-1 focus:ring-accent/50 focus:border-accent/50 transition-colors"
            />
            {inputError && (
              <p className="text-xs text-danger mt-1 flex items-center gap-1">
                <AlertCircle className="w-3 h-3 shrink-0" />
                {inputError}
              </p>
            )}
          </div>
          <button
            type="button"
            onClick={addOrigin}
            disabled={!inputValue.trim()}
            className="p-1.5 rounded-lg border border-border bg-surface-2 text-text-muted hover:text-text-primary hover:bg-surface-3 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
            aria-label="Add origin"
          >
            <Plus className="w-3.5 h-3.5" />
          </button>
        </div>
      )}
    </div>
  );
}

// ── Show-once signing key reveal ─────────────────────────────────────────────

interface SigningKeyRevealProps {
  reveal: PortalSigningKeyReveal;
  onAcknowledge: () => void;
}

function SigningKeyReveal({ reveal, onAcknowledge }: SigningKeyRevealProps) {
  return (
    <div className="space-y-4">
      <div className="rounded-lg border border-warning/30 bg-warning-soft p-4 space-y-3">
        <div className="flex items-start gap-2">
          <AlertCircle className="w-4 h-4 text-warning shrink-0 mt-0.5" />
          <p className="text-sm font-semibold text-warning">Save this signing key — you will not see it again</p>
        </div>
        <div className="flex items-center gap-2 bg-surface-0 border border-border rounded-lg px-3 py-2">
          <code className="text-xs font-mono text-text-primary flex-1 break-all select-all">
            {reveal.signingKey}
          </code>
          <CopyButton text={reveal.signingKey} />
        </div>
        <p className="text-xs text-text-secondary leading-relaxed">
          Store this in your host SaaS backend's environment as{" "}
          <code className="font-mono bg-surface-0 px-1 py-0.5 rounded text-text-primary">PORTAL_SIGNING_KEY</code>.
          Once you click "I've saved it" below, the key will not be retrievable from this dashboard again
          — you'd have to rotate to get a new one.
        </p>
      </div>

      <button
        onClick={onAcknowledge}
        className="w-full inline-flex items-center justify-center gap-1.5 text-sm font-medium px-4 py-2 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 transition-colors"
      >
        <CheckCircle2 className="w-4 h-4" />
        I've saved it
      </button>
    </div>
  );
}

// ── Inner modal body — mounted fresh on each open via key prop ────────────────
// Keeping all ephemeral state here means we get free reset on every open
// without needing useEffect-driven setState calls (which trigger the
// react-hooks/set-state-in-effect lint rule).

interface PortalModalBodyProps {
  appId: string;
  appName: string;
  onClose: () => void;
}

function PortalModalBody({ appId, appName, onClose }: PortalModalBodyProps) {
  const queryClient = useQueryClient();

  // Key reveal is ephemeral — never cached; starts null, set by enable/rotate
  const [keyReveal, setKeyReveal] = useState<PortalSigningKeyReveal | null>(null);

  // Local origin list — initialized from first successful query fetch.
  // `null` means "not yet initialized from server"; we render skeleton until set.
  const [localOrigins, setLocalOrigins] = useState<string[] | null>(null);
  const [originsError, setOriginsError] = useState<string | null>(null);

  // Confirm modal state
  const [confirmAction, setConfirmAction] = useState<"rotate" | "disable" | null>(null);

  // Mutation error displayed inline in the status section
  const [mutationError, setMutationError] = useState<string | null>(null);

  const portalQuery = useQuery({
    queryKey: ["portal-access", appId],
    queryFn: () => getPortalAccess(appId),
    // Don't re-fetch while the key-reveal banner is showing; the data is
    // already stale until the operator acknowledges and we re-enable the query.
    enabled: !keyReveal,
    staleTime: 30_000,
    // Initialize local origins from server on first successful fetch.
    // Using `select` here would lose the ability to detect dirty state, so
    // we rely on the onSuccess equivalent: structuring the component so that
    // localOrigins starts null and we fill it from portal data in render.
  });

  const portal = portalQuery.data;

  // Derive effective local origins: use edited copy if present, otherwise
  // fall back to server data. This avoids useEffect-driven setState.
  const effectiveOrigins: string[] = localOrigins ?? portal?.allowedOrigins ?? [];

  const invalidatePortal = () =>
    queryClient.invalidateQueries({ queryKey: ["portal-access", appId] });

  const enableMutation = useMutation({
    mutationFn: () => enablePortal(appId),
    onSuccess: (result) => {
      setKeyReveal(result);
      setMutationError(null);
      // Reset local origins so next render picks up server data after acknowledge
      setLocalOrigins(null);
      invalidatePortal();
    },
    onError: (e: unknown) =>
      setMutationError(e instanceof Error ? e.message : "Failed to enable portal access")
  });

  const rotateMutation = useMutation({
    mutationFn: () => rotatePortalKey(appId),
    onSuccess: (result) => {
      setKeyReveal(result);
      setMutationError(null);
      setLocalOrigins(null);
      invalidatePortal();
    },
    onError: (e: unknown) =>
      setMutationError(e instanceof Error ? e.message : "Failed to rotate portal key")
  });

  const disableMutation = useMutation({
    mutationFn: () => disablePortal(appId),
    onSuccess: () => {
      setMutationError(null);
      setLocalOrigins(null);
      invalidatePortal();
    },
    onError: (e: unknown) =>
      setMutationError(e instanceof Error ? e.message : "Failed to disable portal access")
  });

  const originsMutation = useMutation({
    mutationFn: (origins: string[]) => updatePortalOrigins(appId, origins),
    onSuccess: (result) => {
      // Replace local copy with canonicalized server response; marks clean.
      setLocalOrigins(result.allowedOrigins);
      setOriginsError(null);
      invalidatePortal();
    },
    onError: (e: unknown) => {
      if (e instanceof ApiErrorException && e.fieldErrors) {
        const firstFieldError = Object.values(e.fieldErrors)[0];
        setOriginsError(firstFieldError ?? e.message);
      } else {
        setOriginsError(e instanceof Error ? e.message : "Failed to save origins");
      }
    }
  });

  const isBusy =
    enableMutation.isPending ||
    rotateMutation.isPending ||
    disableMutation.isPending ||
    originsMutation.isPending;

  // Origins are dirty when the local copy has been set by the user AND differs
  // from the last known server state.
  const savedOrigins = portal?.allowedOrigins ?? [];
  const originsChanged =
    localOrigins !== null &&
    JSON.stringify([...localOrigins].sort()) !== JSON.stringify([...savedOrigins].sort());

  const handleConfirmRotate = () => {
    setMutationError(null);
    rotateMutation.mutate();
  };

  const handleConfirmDisable = () => {
    setMutationError(null);
    disableMutation.mutate();
  };

  const handleSaveOrigins = () => {
    setOriginsError(null);
    originsMutation.mutate(effectiveOrigins);
  };

  return (
    <>
      <Modal
        open
        onClose={onClose}
        title={`Portal access — ${appName}`}
        width="max-w-2xl"
      >
        {/* Key reveal view — replaces all other content until acknowledged */}
        {keyReveal ? (
          <SigningKeyReveal
            reveal={keyReveal}
            onAcknowledge={() => setKeyReveal(null)}
          />
        ) : (
          <div className="space-y-4">
            {/* Loading / fetch error */}
            {portalQuery.isLoading && (
              <div className="flex items-center justify-center py-8">
                <span className="text-sm text-text-muted">Loading portal settings...</span>
              </div>
            )}

            {portalQuery.error && !portalQuery.isLoading && (
              <div className="flex items-center gap-2 text-danger text-xs bg-danger-soft border border-danger/20 rounded-lg px-3 py-2">
                <AlertCircle className="w-3.5 h-3.5 shrink-0" />
                {portalQuery.error instanceof Error
                  ? portalQuery.error.message
                  : "Failed to load portal settings"}
              </div>
            )}

            {/* Mutation error */}
            {mutationError && (
              <div className="flex items-center gap-2 text-danger text-xs bg-danger-soft border border-danger/20 rounded-lg px-3 py-2">
                <AlertCircle className="w-3.5 h-3.5 shrink-0" />
                {mutationError}
                <button
                  onClick={() => setMutationError(null)}
                  className="ml-auto text-danger/60 hover:text-danger"
                >
                  <X className="w-3 h-3" />
                </button>
              </div>
            )}

            {portal && (
              <>
                {/* ── Status section ─────────────────────────────────── */}
                <div className="rounded-lg border border-border bg-surface-1 p-4 space-y-3">
                  <h3 className="text-xs font-semibold text-text-muted uppercase tracking-wide">Status</h3>

                  {portal.portalEnabled ? (
                    <>
                      <div className="flex items-center gap-2">
                        <Globe className="w-4 h-4 text-success" />
                        <span className="inline-flex items-center gap-1 text-xs font-medium px-1.5 py-0.5 rounded bg-success-soft text-success">
                          Enabled
                        </span>
                        {portal.rotatedAt && (
                          <span className="text-xs text-text-muted">
                            Last rotated {formatLocaleDateTime(portal.rotatedAt)}
                          </span>
                        )}
                      </div>
                      <div className="flex items-center gap-2">
                        <button
                          onClick={() => setConfirmAction("rotate")}
                          disabled={isBusy}
                          className="text-xs font-medium px-3 py-1.5 rounded-lg border border-border bg-surface-2 text-text-secondary hover:text-text-primary hover:bg-surface-3 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                        >
                          Rotate signing key
                        </button>
                        <button
                          onClick={() => setConfirmAction("disable")}
                          disabled={isBusy}
                          className="text-xs font-medium px-3 py-1.5 rounded-lg bg-danger-soft text-danger hover:bg-danger/20 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                        >
                          Disable portal
                        </button>
                      </div>
                    </>
                  ) : (
                    <>
                      <div className="flex items-center gap-2">
                        <Globe className="w-4 h-4 text-text-muted" />
                        <span className="text-xs text-text-muted">
                          Portal access is disabled. Enable it to let your customers manage
                          their own endpoints via the embedded component.
                        </span>
                      </div>
                      <button
                        onClick={() => enableMutation.mutate()}
                        disabled={isBusy}
                        className="text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                      >
                        {enableMutation.isPending ? "Enabling..." : "Enable portal access"}
                      </button>
                    </>
                  )}
                </div>

                {/* ── Allowed origins + Embed snippet (only when enabled) ─── */}
                {portal.portalEnabled && (
                  <>
                    {/* Allowed origins */}
                    <div className="rounded-lg border border-border bg-surface-1 p-4 space-y-3">
                      <h3 className="text-xs font-semibold text-text-muted uppercase tracking-wide">
                        Allowed origins
                      </h3>
                      <p className="text-xs text-text-secondary">
                        CORS origins that may embed the portal component. Add the exact
                        scheme + hostname of your SaaS frontend (no paths, no wildcards).
                      </p>

                      <OriginChipList
                        origins={effectiveOrigins}
                        onChange={setLocalOrigins}
                        disabled={isBusy}
                      />

                      {originsError && (
                        <p className="text-xs text-danger flex items-center gap-1">
                          <AlertCircle className="w-3 h-3 shrink-0" />
                          {originsError}
                        </p>
                      )}

                      <div className="flex justify-end">
                        <button
                          onClick={handleSaveOrigins}
                          disabled={!originsChanged || isBusy}
                          className="text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                        >
                          {originsMutation.isPending ? "Saving..." : "Save changes"}
                        </button>
                      </div>
                    </div>

                    {/* Embed snippet */}
                    <div className="rounded-lg border border-border bg-surface-1 p-4 space-y-3">
                      <h3 className="text-xs font-semibold text-text-muted uppercase tracking-wide">
                        Embed snippet
                      </h3>

                      <CodeBlock
                        label="Backend — mint a portal token (Node / TypeScript)"
                        code={MINT_SNIPPET}
                      />

                      <CodeBlock
                        label="Frontend — render the React component"
                        code={RENDER_SNIPPET}
                      />

                      <p className="text-xs text-text-muted border-t border-border-subtle pt-2">
                        Package{" "}
                        <code className="font-mono bg-surface-0 px-1 py-0.5 rounded text-text-primary">
                          @webhookengine/endpoint-manager
                        </code>{" "}
                        will be published in B1 Step 7 (not yet available on npm).
                      </p>
                    </div>
                  </>
                )}
              </>
            )}
          </div>
        )}
      </Modal>

      {/* Rotate confirm */}
      <ConfirmModal
        open={confirmAction === "rotate"}
        onClose={() => setConfirmAction(null)}
        onConfirm={handleConfirmRotate}
        title="Rotate signing key"
        description="Existing portal sessions using the current key will stop working immediately. Continue?"
        confirmLabel="Rotate key"
        variant="default"
        loading={rotateMutation.isPending}
      />

      {/* Disable confirm */}
      <ConfirmModal
        open={confirmAction === "disable"}
        onClose={() => setConfirmAction(null)}
        onConfirm={handleConfirmDisable}
        title="Disable portal access"
        description="Customers embedding <EndpointManager /> will lose access. The signing key and allowed-origin list will both be cleared. Continue?"
        confirmLabel="Disable portal"
        variant="danger"
        loading={disableMutation.isPending}
      />
    </>
  );
}

// ── Public export ─────────────────────────────────────────────────────────────
// The outer shell is just an open/closed gate. When open=true we mount
// PortalModalBody with a stable key (appId) so that switching between
// different apps via the table never bleeds state from one app to another.

export interface PortalAccessModalProps {
  open: boolean;
  onClose: () => void;
  appId: string;
  appName: string;
}

export function PortalAccessModal({ open, onClose, appId, appName }: PortalAccessModalProps) {
  if (!open) return null;
  return (
    <PortalModalBody
      key={appId}
      appId={appId}
      appName={appName}
      onClose={onClose}
    />
  );
}
