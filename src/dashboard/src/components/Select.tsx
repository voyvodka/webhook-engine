import { useEffect, useRef, useState } from "react";
import { ChevronDown, Check } from "lucide-react";

export interface SelectOption {
  value: string;
  label: string;
}

interface SelectProps {
  value: string;
  onChange: (value: string) => void;
  options: SelectOption[];
  placeholder?: string;
}

export function Select({ value, onChange, options, placeholder = "Select..." }: SelectProps) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  const selected = options.find((o) => o.value === value);

  // Close on click outside
  useEffect(() => {
    if (!open) return;
    const handler = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [open]);

  // Close on Escape
  useEffect(() => {
    if (!open) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, [open]);

  return (
    <div ref={containerRef} className="relative">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className={`w-full flex items-center justify-between gap-2 px-3 py-2 text-sm bg-surface-2 border rounded-lg transition-colors cursor-pointer text-left ${
          open
            ? "border-accent/50 ring-1 ring-accent/50"
            : "border-border hover:border-surface-4"
        }`}
      >
        <span className={selected ? "text-text-primary truncate" : "text-text-muted truncate"}>
          {selected?.label ?? placeholder}
        </span>
        <ChevronDown className={`w-3.5 h-3.5 shrink-0 text-text-muted transition-transform duration-150 ${open ? "rotate-180" : ""}`} />
      </button>

      {open && (
        <div className="absolute z-40 mt-1 w-full min-w-[160px] max-h-52 overflow-auto rounded-lg border border-border bg-surface-1 shadow-xl shadow-black/30 py-1">
          {options.map((option) => {
            const isSelected = option.value === value;
            return (
              <button
                key={option.value}
                type="button"
                onClick={() => {
                  onChange(option.value);
                  setOpen(false);
                }}
                className={`w-full flex items-center gap-2 px-3 py-1.5 text-sm text-left transition-colors ${
                  isSelected
                    ? "text-accent bg-accent-soft"
                    : "text-text-secondary hover:text-text-primary hover:bg-surface-2"
                }`}
              >
                <span className="flex-1 truncate">{option.label}</span>
                {isSelected && <Check className="w-3 h-3 shrink-0 text-accent" />}
              </button>
            );
          })}
          {options.length === 0 && (
            <p className="px-3 py-2 text-xs text-text-muted">No options</p>
          )}
        </div>
      )}
    </div>
  );
}
