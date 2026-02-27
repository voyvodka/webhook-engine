import { useEffect, useRef, useState, useCallback } from "react";
import { HubConnectionBuilder, HubConnection, LogLevel, HubConnectionState } from "@microsoft/signalr";

export interface DeliveryEvent {
  messageId: string;
  endpointId: string;
  attemptCount: number;
  status: "Delivered" | "Failed" | "DeadLetter";
  latencyMs?: number;
  error?: string;
  timestamp: string;
}

/**
 * Connects to the SignalR /hubs/deliveries endpoint and provides
 * a rolling list of recent delivery events (most recent first).
 */
export function useDeliveryFeed(maxEvents = 50) {
  const connectionRef = useRef<HubConnection | null>(null);
  const [events, setEvents] = useState<DeliveryEvent[]>([]);
  const [connected, setConnected] = useState(false);

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
      .withAutomaticReconnect()
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

    const handleClose = () => {
      if (isMounted) {
        setConnected(false);
      }
    };

    const handleReconnected = () => {
      if (isMounted) {
        setConnected(true);
      }
    };

    connection.on("DeliverySuccess", handleSuccess);
    connection.on("DeliveryFailure", handleFailure);
    connection.on("DeadLetter", handleDeadLetter);
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

      if (connection.state !== HubConnectionState.Disconnected) {
        void connection.stop();
      }
    };
  }, [push]);

  return { events, connected };
}
