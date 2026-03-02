type DateInput = string | number | Date | null | undefined;

interface FormatOptions {
  fallback?: string;
}

interface TimeFormatOptions extends FormatOptions {
  withSeconds?: boolean;
}

const formatterCache = new Map<string, Intl.DateTimeFormat>();

function resolveLocale(): string | undefined {
  if (typeof navigator === "undefined") {
    return undefined;
  }

  if (navigator.languages && navigator.languages.length > 0) {
    return navigator.languages[0];
  }

  return navigator.language;
}

function getFormatter(options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const locale = resolveLocale();
  const key = `${locale ?? "default"}|${JSON.stringify(options)}`;

  const cached = formatterCache.get(key);
  if (cached) {
    return cached;
  }

  const formatter = new Intl.DateTimeFormat(locale, options);
  formatterCache.set(key, formatter);
  return formatter;
}

function toDate(value: DateInput): Date | null {
  if (value === null || value === undefined) {
    return null;
  }

  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) {
    return null;
  }

  return date;
}

export function formatLocaleDate(value: DateInput, options: FormatOptions = {}): string {
  const date = toDate(value);
  if (!date) {
    return options.fallback ?? "--";
  }

  return getFormatter({ dateStyle: "short" }).format(date);
}

export function formatLocaleDateTime(value: DateInput, options: FormatOptions = {}): string {
  const date = toDate(value);
  if (!date) {
    return options.fallback ?? "--";
  }

  return getFormatter({ dateStyle: "short", timeStyle: "medium" }).format(date);
}

export function formatLocaleTime(value: DateInput, options: TimeFormatOptions = {}): string {
  const date = toDate(value);
  if (!date) {
    return options.fallback ?? "--";
  }

  return getFormatter({ timeStyle: options.withSeconds ? "medium" : "short" }).format(date);
}
