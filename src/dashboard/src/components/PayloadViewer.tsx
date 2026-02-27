interface PayloadViewerProps {
  value: unknown;
}

export function PayloadViewer({ value }: PayloadViewerProps) {
  return (
    <pre className="font-mono text-xs leading-relaxed bg-surface-0 text-accent border border-border rounded-lg p-3 overflow-auto max-h-72 whitespace-pre-wrap break-all">
      {JSON.stringify(value, null, 2)}
    </pre>
  );
}
