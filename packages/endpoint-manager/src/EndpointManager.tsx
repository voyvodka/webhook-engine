import type { JSX } from "react";
import type { EndpointManagerProps } from "./types.js";

export function EndpointManager(props: EndpointManagerProps): JSX.Element {
  void props; // referenced to keep noUnusedParameters happy
  return (
    <div
      style={{
        padding: "1.5rem",
        borderRadius: "0.75rem",
        border: "1px solid rgba(127, 127, 127, 0.25)",
        fontFamily: "system-ui, -apple-system, sans-serif",
        fontSize: "0.875rem",
        lineHeight: 1.5,
        color: "rgba(120, 120, 120, 0.9)"
      }}
    >
      <strong style={{ display: "block", marginBottom: "0.5rem", color: "rgba(80, 80, 80, 0.95)" }}>
        WebhookEngine portal
      </strong>
      <span>
        The interactive components (endpoint list, editor, tester, attempt history) ship in B1
        Step 8 of the WebhookEngine roadmap. This package is currently a placeholder — install it
        early to lock the import path; it will gain the real UI in{" "}
        <code>portal-v0.1.0</code>.
      </span>
    </div>
  );
}
