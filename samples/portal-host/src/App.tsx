import { EndpointManager } from "@webhookengine/endpoint-manager";
import { useEffect, useState } from "react";
import { installMockFetch } from "./mock-fetch.js";
import { mintPortalToken } from "./mint-token.js";

// Install the fetch shim before any component renders.
// In a real host SaaS app this line is removed — the real engine handles requests.
installMockFetch();

// This UUID identifies the customer's WebhookEngine Application.
// In production it comes from your SaaS database (the row that maps a
// customer account to its WebhookEngine appId).
const SAMPLE_APP_ID = "00000000-0000-0000-0000-0000000000aa";

export function App() {
  const [token, setToken] = useState<string | null>(null);

  useEffect(() => {
    // In production this fetch goes to your SaaS backend's own
    // POST /internal/portal-token endpoint, which is the only place
    // that holds the per-app PORTAL_SIGNING_KEY. The browser never
    // sees the signing key; it only receives the short-lived JWT.
    void mintPortalToken(SAMPLE_APP_ID, [
      "endpoints:read",
      "endpoints:write",
      "endpoints:test",
      "attempts:read",
    ]).then(setToken);
  }, []);

  return (
    <main className="page">
      <header>
        <h1>Acme SaaS — Webhook settings</h1>
        <p>
          This page lives in the host SaaS app. The portal below is the
          embedded <code>@webhookengine/endpoint-manager</code> component.
        </p>
      </header>

      {token ? (
        <EndpointManager
          baseUrl="https://hooks.example.com"
          token={token}
          appId={SAMPLE_APP_ID}
          capabilities={[
            "endpoints:read",
            "endpoints:write",
            "endpoints:test",
            "attempts:read",
          ]}
        />
      ) : (
        <p className="loading">Loading portal token…</p>
      )}
    </main>
  );
}
