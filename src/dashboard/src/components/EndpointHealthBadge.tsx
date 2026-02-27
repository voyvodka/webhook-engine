import { Circle } from "lucide-react";
import type { CircuitState } from "../types";

const config: Record<string, { label: string; dotClass: string; badgeClass: string }> = {
  closed: {
    label: "Active",
    dotClass: "text-success",
    badgeClass: "text-success bg-success-soft"
  },
  halfopen: {
    label: "Degraded",
    dotClass: "text-warning",
    badgeClass: "text-warning bg-warning-soft"
  },
  open: {
    label: "Failed",
    dotClass: "text-danger",
    badgeClass: "text-danger bg-danger-soft"
  }
};

export function EndpointHealthBadge({ state }: { state: CircuitState | string }) {
  const key = state.toLowerCase();
  const { label, dotClass, badgeClass } = config[key] ?? config.closed;

  return (
    <span className={`inline-flex items-center gap-1.5 text-xs font-medium px-2 py-0.5 rounded-full ${badgeClass}`}>
      <Circle className={`w-2 h-2 fill-current ${dotClass}`} />
      {label}
    </span>
  );
}
