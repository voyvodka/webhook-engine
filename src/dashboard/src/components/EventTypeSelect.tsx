import { Check } from "lucide-react";
import type { EventTypeSummary } from "../types";

interface EventTypeSelectProps {
  eventTypes: EventTypeSummary[];
  selected: string[];
  onChange: (ids: string[]) => void;
}

export function EventTypeSelect({ eventTypes, selected, onChange }: EventTypeSelectProps) {
  const toggle = (id: string) => {
    onChange(
      selected.includes(id)
        ? selected.filter((s) => s !== id)
        : [...selected, id]
    );
  };

  if (eventTypes.length === 0) {
    return (
      <p className="text-xs text-text-muted py-2">No event types defined for this application.</p>
    );
  }

  return (
    <div className="space-y-1.5">
      <div className="flex flex-wrap gap-1.5">
        {eventTypes.map((et) => {
          const isSelected = selected.includes(et.id);
          return (
            <button
              key={et.id}
              type="button"
              onClick={() => toggle(et.id)}
              className={`inline-flex items-center gap-1 text-xs font-medium px-2.5 py-1 rounded-md border transition-colors ${
                isSelected
                  ? "border-accent/40 bg-accent-soft text-accent"
                  : "border-border bg-surface-2 text-text-secondary hover:text-text-primary hover:border-border"
              }`}
            >
              {isSelected && <Check className="w-3 h-3" />}
              {et.name}
            </button>
          );
        })}
      </div>
      {selected.length > 0 && (
        <button
          type="button"
          onClick={() => onChange([])}
          className="text-xs text-text-muted hover:text-text-primary transition-colors"
        >
          Clear all ({selected.length} selected)
        </button>
      )}
    </div>
  );
}
