import { Modal } from "./Modal";
import { AlertTriangle } from "lucide-react";

interface ConfirmModalProps {
  open: boolean;
  onClose: () => void;
  onConfirm: () => void;
  title: string;
  description: string;
  /** Button label, defaults to "Confirm" */
  confirmLabel?: string;
  /** "danger" shows red button, "default" shows accent */
  variant?: "danger" | "default";
  loading?: boolean;
}

export function ConfirmModal({
  open,
  onClose,
  onConfirm,
  title,
  description,
  confirmLabel = "Confirm",
  variant = "default",
  loading = false
}: ConfirmModalProps) {
  const isDanger = variant === "danger";

  return (
    <Modal open={open} onClose={onClose} title={title} width="max-w-sm">
      <div className="space-y-4">
        <div className="flex gap-3">
          <div className={`shrink-0 w-8 h-8 rounded-lg flex items-center justify-center ${isDanger ? "bg-danger-soft" : "bg-warning-soft"}`}>
            <AlertTriangle className={`w-4 h-4 ${isDanger ? "text-danger" : "text-warning"}`} />
          </div>
          <p className="text-sm text-text-secondary leading-relaxed">{description}</p>
        </div>

        <div className="flex items-center justify-end gap-2 pt-1">
          <button
            onClick={onClose}
            disabled={loading}
            className="text-xs font-medium px-3 py-1.5 rounded-lg border border-border bg-surface-2 text-text-secondary hover:text-text-primary hover:bg-surface-3 disabled:opacity-50 transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={() => { onConfirm(); onClose(); }}
            disabled={loading}
            className={`text-xs font-medium px-3 py-1.5 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed transition-colors ${
              isDanger
                ? "bg-danger text-white hover:bg-danger/90"
                : "bg-accent text-zinc-950 hover:bg-accent/90"
            }`}
          >
            {loading ? "Processing..." : confirmLabel}
          </button>
        </div>
      </div>
    </Modal>
  );
}
