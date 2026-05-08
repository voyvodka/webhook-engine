export type StatusBadgeKind =
  | "delivered"
  | "pending"
  | "sending"
  | "failed"
  | "deadletter"
  | "active"
  | "degraded"
  | "disabled";

interface StatusBadgeProps {
  kind: StatusBadgeKind | string;
  label?: string;
}

interface BadgeConfig {
  label: string;
  badgeClass: string;
}

// Canonical key is lowercase; callers may pass PascalCase backend strings
const CONFIG: Record<string, BadgeConfig> = {
  delivered: { label: "Delivered", badgeClass: "text-success bg-success-soft" },
  pending: { label: "Pending", badgeClass: "text-warning bg-warning-soft" },
  sending: { label: "Sending", badgeClass: "text-accent bg-accent-soft" },
  failed: { label: "Failed", badgeClass: "text-danger bg-danger-soft" },
  deadletter: { label: "Dead Letter", badgeClass: "text-danger bg-danger-soft" },
  active: { label: "Active", badgeClass: "text-success bg-success-soft" },
  degraded: { label: "Degraded", badgeClass: "text-warning bg-warning-soft" },
  disabled: { label: "Disabled", badgeClass: "text-text-muted bg-surface-2" }
};

export function StatusBadge({ kind, label }: StatusBadgeProps) {
  const key = kind.toLowerCase();
  const config = CONFIG[key] ?? { label: kind, badgeClass: "text-text-muted bg-surface-2" };
  const displayLabel = label ?? config.label;

  return (
    <span
      className={`inline-block text-xs font-medium px-1.5 py-0.5 rounded ${config.badgeClass}`}
    >
      {displayLabel}
    </span>
  );
}
