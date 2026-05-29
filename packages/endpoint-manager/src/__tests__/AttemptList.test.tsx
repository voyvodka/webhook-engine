import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AttemptList } from "../components/AttemptList.js";
import type { PortalClient, PortalListResult } from "../api/createPortalClient.js";
import type { PortalAttempt, PortalEndpointSummary } from "../types.js";

const ENDPOINT: PortalEndpointSummary = {
  id: "ep-1",
  url: "https://consumer.example.com/hooks",
  description: "Test endpoint",
  status: "active",
  hasSecretOverride: false,
  filterEventTypes: [],
  createdAt: "2026-01-01T00:00:00Z",
};

function makeAttempt(overrides: Partial<PortalAttempt> = {}): PortalAttempt {
  return {
    id: "att-1",
    messageId: "msg-1",
    attemptNumber: 1,
    status: "success",
    statusCode: 200,
    error: null,
    latencyMs: 142,
    createdAt: new Date().toISOString(),
    ...overrides,
  };
}

function makeClient(overrides: Partial<PortalClient> = {}): PortalClient {
  return {
    listEndpoints: vi.fn(),
    getEndpoint: vi.fn(),
    createEndpoint: vi.fn(),
    updateEndpoint: vi.fn(),
    deleteEndpoint: vi.fn(),
    enableEndpoint: vi.fn(),
    disableEndpoint: vi.fn(),
    listEventTypes: vi.fn(),
    testEndpoint: vi.fn(),
    listAttempts: vi.fn().mockResolvedValue({
      data: [makeAttempt()],
      pagination: { page: 1, pageSize: 20, total: 1 },
    } satisfies PortalListResult<PortalAttempt>),
    ...overrides,
  };
}

describe("AttemptList", () => {
  const onClose = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders attempt rows from client.listAttempts", async () => {
    const client = makeClient();
    render(
      <AttemptList
        client={client}
        endpoint={ENDPOINT}
        capabilities={["attempts:read"]}
        onClose={onClose}
      />,
    );

    await waitFor(() =>
      expect(screen.getByText("200")).toBeInTheDocument(),
    );
    expect(screen.getByText(/142 ms/i)).toBeInTheDocument();
  });

  it("status badge is green for Success, red for Failed", async () => {
    const client = makeClient({
      listAttempts: vi.fn().mockResolvedValue({
        data: [
          makeAttempt({ id: "att-1", status: "success" }),
          makeAttempt({ id: "att-2", status: "failed", statusCode: 500 }),
        ],
        pagination: { page: 1, pageSize: 20, total: 2 },
      }),
    });
    render(
      <AttemptList
        client={client}
        endpoint={ENDPOINT}
        capabilities={["attempts:read"]}
        onClose={onClose}
      />,
    );

    await waitFor(() => expect(screen.getByText("Success")).toBeInTheDocument());
    expect(screen.getByText("Failed")).toBeInTheDocument();

    const successBadge = screen.getByText("Success").closest("span");
    const failedBadge = screen.getByText("Failed").closest("span");

    expect(successBadge?.className).toContain("whe-success");
    expect(failedBadge?.className).toContain("whe-danger");
  });

  it("Previous is disabled on page 1", async () => {
    const client = makeClient({
      listAttempts: vi.fn().mockResolvedValue({
        data: Array.from({ length: 20 }, (_, i) =>
          makeAttempt({ id: `att-${i}`, attemptNumber: i + 1 }),
        ),
        pagination: { page: 1, pageSize: 20, total: 50 },
      }),
    });
    render(
      <AttemptList
        client={client}
        endpoint={ENDPOINT}
        capabilities={["attempts:read"]}
        onClose={onClose}
      />,
    );

    await waitFor(() => screen.getByRole("button", { name: /Previous/i }));
    expect(screen.getByRole("button", { name: /Previous/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Next/i })).not.toBeDisabled();
  });

  it("Next is disabled on the last page", async () => {
    const user = userEvent.setup();
    const listAttempts = vi
      .fn()
      .mockResolvedValueOnce({
        data: Array.from({ length: 20 }, (_, i) =>
          makeAttempt({ id: `att-${i}`, attemptNumber: i + 1 }),
        ),
        pagination: { page: 1, pageSize: 20, total: 25 },
      })
      .mockResolvedValueOnce({
        data: Array.from({ length: 5 }, (_, i) =>
          makeAttempt({ id: `att-page2-${i}`, attemptNumber: i + 21 }),
        ),
        pagination: { page: 2, pageSize: 20, total: 25 },
      });

    const client = makeClient({ listAttempts });
    render(
      <AttemptList
        client={client}
        endpoint={ENDPOINT}
        capabilities={["attempts:read"]}
        onClose={onClose}
      />,
    );

    await waitFor(() => screen.getByRole("button", { name: /Next/i }));
    await user.click(screen.getByRole("button", { name: /Next/i }));
    await waitFor(() => expect(listAttempts).toHaveBeenCalledTimes(2));

    expect(screen.getByRole("button", { name: /Next/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Previous/i })).not.toBeDisabled();
  });

  it("renders empty state when 0 attempts are returned", async () => {
    const client = makeClient({
      listAttempts: vi.fn().mockResolvedValue({
        data: [],
        pagination: { page: 1, pageSize: 20, total: 0 },
      }),
    });
    render(
      <AttemptList
        client={client}
        endpoint={ENDPOINT}
        capabilities={["attempts:read"]}
        onClose={onClose}
      />,
    );

    await waitFor(() =>
      expect(
        screen.getByText(/No delivery attempts recorded/i),
      ).toBeInTheDocument(),
    );
  });

  it("renders capability banner when attempts:read is missing", async () => {
    const client = makeClient();
    render(
      <AttemptList
        client={client}
        endpoint={ENDPOINT}
        capabilities={["endpoints:read"]}
        onClose={onClose}
      />,
    );

    expect(screen.getByText(/Capability required/i)).toBeInTheDocument();
    expect(screen.getByText(/attempts:read/i)).toBeInTheDocument();
    // listAttempts should NOT have been called.
    expect(client.listAttempts).not.toHaveBeenCalled();
  });

  it("Close button calls onClose", async () => {
    const user = userEvent.setup();
    const client = makeClient();
    render(
      <AttemptList
        client={client}
        endpoint={ENDPOINT}
        capabilities={["attempts:read"]}
        onClose={onClose}
      />,
    );

    await user.click(screen.getByRole("button", { name: /Close/i }));
    expect(onClose).toHaveBeenCalledOnce();
  });
});
