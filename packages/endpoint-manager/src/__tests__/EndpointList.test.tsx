import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { EndpointList } from "../components/EndpointList.js";
import type { PortalClient, PortalListResult } from "../api/createPortalClient.js";
import type { PortalEndpointSummary } from "../types.js";

function makeEndpoint(overrides: Partial<PortalEndpointSummary> = {}): PortalEndpointSummary {
  return {
    id: "ep-1",
    url: "https://consumer.example.com/hooks",
    description: "Test endpoint",
    status: "active",
    hasSecretOverride: false,
    filterEventTypes: [],
    createdAt: "2026-01-01T00:00:00Z",
    ...overrides,
  };
}

function makeClient(
  overrides: Partial<PortalClient> = {},
): PortalClient {
  return {
    listEndpoints: vi.fn().mockResolvedValue({
      data: [makeEndpoint()],
      pagination: { page: 1, pageSize: 20, total: 1 },
    } satisfies PortalListResult<PortalEndpointSummary>),
    getEndpoint: vi.fn(),
    createEndpoint: vi.fn(),
    updateEndpoint: vi.fn(),
    deleteEndpoint: vi.fn().mockResolvedValue(undefined),
    enableEndpoint: vi.fn().mockResolvedValue(undefined),
    disableEndpoint: vi.fn().mockResolvedValue(undefined),
    listEventTypes: vi.fn().mockResolvedValue([]),
    testEndpoint: vi.fn(),
    listAttempts: vi.fn(),
    ...overrides,
  };
}

describe("EndpointList", () => {
  const onEditEndpoint = vi.fn();
  const onNewEndpoint = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders endpoint rows from the client", async () => {
    const client = makeClient();
    render(
      <EndpointList
        client={client}
        appId="app-1"
        capabilities={["endpoints:read", "endpoints:write"]}
        onEditEndpoint={onEditEndpoint}
        onNewEndpoint={onNewEndpoint}
      />,
    );

    await waitFor(() =>
      expect(screen.getByText(/consumer\.example\.com/)).toBeInTheDocument(),
    );
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("shows '+ New endpoint' button only when endpoints:write capability is present", async () => {
    const client = makeClient();

    const { rerender } = render(
      <EndpointList
        client={client}
        appId="app-1"
        capabilities={["endpoints:read"]}
        onEditEndpoint={onEditEndpoint}
        onNewEndpoint={onNewEndpoint}
      />,
    );

    await waitFor(() =>
      expect(screen.getByText(/consumer\.example\.com/)).toBeInTheDocument(),
    );
    expect(screen.queryByText(/New endpoint/)).not.toBeInTheDocument();

    rerender(
      <EndpointList
        client={client}
        appId="app-1"
        capabilities={["endpoints:read", "endpoints:write"]}
        onEditEndpoint={onEditEndpoint}
        onNewEndpoint={onNewEndpoint}
      />,
    );

    await waitFor(() =>
      expect(screen.getByText(/New endpoint/)).toBeInTheDocument(),
    );
  });

  it("shows empty state with CTA when no endpoints are returned", async () => {
    const client = makeClient({
      listEndpoints: vi.fn().mockResolvedValue({
        data: [],
        pagination: { page: 1, pageSize: 20, total: 0 },
      }),
    });

    render(
      <EndpointList
        client={client}
        appId="app-1"
        capabilities={["endpoints:read", "endpoints:write"]}
        onEditEndpoint={onEditEndpoint}
        onNewEndpoint={onNewEndpoint}
      />,
    );

    await waitFor(() =>
      expect(screen.getByText(/No endpoints yet/)).toBeInTheDocument(),
    );
    expect(screen.getByText(/Create your first endpoint/)).toBeInTheDocument();
  });

  it("calls onNewEndpoint when + New endpoint is clicked", async () => {
    const user = userEvent.setup();
    const client = makeClient();

    render(
      <EndpointList
        client={client}
        appId="app-1"
        capabilities={["endpoints:read", "endpoints:write"]}
        onEditEndpoint={onEditEndpoint}
        onNewEndpoint={onNewEndpoint}
      />,
    );

    await waitFor(() => screen.getByText(/New endpoint/));
    await user.click(screen.getByText(/New endpoint/));
    expect(onNewEndpoint).toHaveBeenCalledOnce();
  });

  it("shows error state and retry button on fetch failure", async () => {
    const user = userEvent.setup();
    const client = makeClient({
      listEndpoints: vi.fn().mockRejectedValue(new Error("Network error")),
    });

    render(
      <EndpointList
        client={client}
        appId="app-1"
        capabilities={["endpoints:read"]}
        onEditEndpoint={onEditEndpoint}
        onNewEndpoint={onNewEndpoint}
      />,
    );

    await waitFor(() => screen.getByText(/Network error/));
    const retry = screen.getByText("Retry");
    expect(retry).toBeInTheDocument();

    // Retry should re-call listEndpoints.
    await user.click(retry);
    expect(client.listEndpoints).toHaveBeenCalledTimes(2);
  });
});
