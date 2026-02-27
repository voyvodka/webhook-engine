import { useState } from "react";
import { RotateCcw, Check, X } from "lucide-react";
import { retryMessage } from "../api/dashboardApi";

interface RetryButtonProps {
  messageId: string;
  onRetried?: () => void;
}

export function RetryButton({ messageId, onRetried }: RetryButtonProps) {
  const [isRetrying, setIsRetrying] = useState(false);
  const [result, setResult] = useState<"idle" | "success" | "error">("idle");

  const handleRetry = async () => {
    setIsRetrying(true);
    setResult("idle");
    try {
      await retryMessage(messageId);
      setResult("success");
      onRetried?.();
    } catch {
      setResult("error");
    } finally {
      setIsRetrying(false);
    }
  };

  if (result === "success") {
    return (
      <span className="inline-flex items-center gap-1 text-xs font-medium text-success bg-success-soft px-2 py-1 rounded-md">
        <Check className="w-3 h-3" />
        Queued
      </span>
    );
  }

  return (
    <button
      onClick={handleRetry}
      disabled={isRetrying}
      className="inline-flex items-center gap-1 text-xs font-medium px-2 py-1 rounded-md border border-border bg-surface-2 text-text-secondary hover:text-text-primary hover:bg-surface-3 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
    >
      {isRetrying ? (
        <>
          <RotateCcw className="w-3 h-3 animate-spin-slow" />
          Retrying...
        </>
      ) : result === "error" ? (
        <>
          <X className="w-3 h-3 text-danger" />
          Failed
        </>
      ) : (
        <>
          <RotateCcw className="w-3 h-3" />
          Retry
        </>
      )}
    </button>
  );
}
