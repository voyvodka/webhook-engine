import { useEffect, useRef, useState, useCallback } from "react";
import { HubConnectionBuilder, HubConnection, LogLevel, HubConnectionState, IRetryPolicy, RetryContext } from "@microsoft/signalr";

export interface DeliveryEvent {
  messageId: string;
  endpointId: string;
  attemptCount: number;
  status: "Delivered" | "Failed" | "DeadLetter";
  latencyMs?: number;
  error?: string;
  timestamp: string;
}

export interface EndpointHealthChange {
  endpointId: string;
  // Lowercase enum string ("active" | "degraded" | "failed" | "disabled") to
  // match the existing dashboard convention.
  status: string;
  // PascalCase enum string ("Closed" | "Open" | "HalfOpen").
  circuitState: string;
  consecutiveFailures: number;
  cooldownUntilUtc: string | null;
  timestamp: string;
}

/** Infinite retry with capped exponential backoff (max 30s). */
const infiniteRetryPolicy: IRetryPolicy = {
  nextRetryDelayInMilliseconds(retryContext: RetryContext) {
    return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30_000);
  },
};

/**
 * Connects to the SignalR /hubs/deliveries endpoint and provides
 * a rolling list of recent delivery events (most recent first).
 */
export function useDeliveryFeed(maxEvents = 50) {
  const connectionRef = useRef<HubConnection | null>(null);
  const [events, setEvents] = useState<DeliveryEvent[]>([]);
  const [connected, setConnected] = useState(false);
  // Single-slot state — pages watch its identity in useEffect to patch
  // their local endpoint state. Storing only the latest change is enough
  // because each change is idempotent against the prior badge value.
  const [lastHealthChange, setLastHealthChange] = useState<EndpointHealthChange | null>(null);

  const push = useCallback(
    (event: DeliveryEvent) => {
      setEvents((prev) => [event, ...prev].slice(0, maxEvents));
    },
    [maxEvents]
  );

  useEffect(() => {
    let isMounted = true;

    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/deliveries")
      .withAutomaticReconnect(infiniteRetryPolicy)
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    const handleSuccess = (data: DeliveryEvent) => {
      push({ ...data, status: "Delivered" });
    };

    const handleFailure = (data: DeliveryEvent) => {
      push({ ...data, status: "Failed" });
    };

    const handleDeadLetter = (data: DeliveryEvent) => {
      push({ ...data, status: "DeadLetter" });
    };

    const handleHealthChange = (data: EndpointHealthChange) => {
      if (!isMounted) return;
      setLastHealthChange(data);
    };

    const handleClose = () => {
      if (!isMounted) return;
      setConnected(false);

      // withAutomaticReconnect only kicks in after a successful connection.
      // If the connection was never established or all built-in retries
      // are exhausted, manually restart with backoff.
      setTimeout(() => {
        if (isMounted && connection.state === HubConnectionState.Disconnected) {
          void connection.start().then(() => {
            if (isMounted) setConnected(true);
          }).catch(() => { /* next onclose will retry */ });
        }
      }, 5_000);
    };

    const handleReconnected = () => {
      if (isMounted) {
        setConnected(true);
      }
    };

    connection.on("DeliverySuccess", handleSuccess);
    connection.on("DeliveryFailure", handleFailure);
    connection.on("DeadLetter", handleDeadLetter);
    connection.on("EndpointHealthChanged", handleHealthChange);
    connection.onclose(handleClose);
    connection.onreconnected(handleReconnected);

    const isExpectedShutdownError = (error: unknown): boolean => {
      const message = error instanceof Error ? error.message : String(error ?? "");
      return message.includes("stopped during negotiation")
        || message.includes("AbortError")
        || message.includes("Connection disconnected");
    };

    void connection
      .start()
      .then(async () => {
        if (!isMounted) {
          if (connection.state !== HubConnectionState.Disconnected) {
            await connection.stop();
          }
          return;
        }

        setConnected(true);
      })
      .catch((err) => {
        if (!isMounted || isExpectedShutdownError(err)) {
          return;
        }

        console.warn("SignalR connection failed:", err);
      });

    return () => {
      isMounted = false;
      setConnected(false);

      connection.off("DeliverySuccess", handleSuccess);
      connection.off("DeliveryFailure", handleFailure);
      connection.off("DeadLetter", handleDeadLetter);
      connection.off("EndpointHealthChanged", handleHealthChange);

      if (connection.state !== HubConnectionState.Disconnected) {
        void connection.stop();
      }
    };
  }, [push]);

  return { events, connected, lastHealthChange };
}
