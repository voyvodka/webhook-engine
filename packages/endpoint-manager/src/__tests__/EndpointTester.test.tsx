import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { EndpointTester } from "../components/EndpointTester.js";
import type { PortalClient } from "../api/createPortalClient.js";
import type { PortalEndpointSummary, PortalTestResult } from "../types.js";

const ENDPOINT: PortalEndpointSummary = {
  id: "ep-1",
  url: "https://consumer.example.com/hooks",
  description: "Test endpoint",
  status: "active",
  hasSecretOverride: false,
  filterEventTypes: [],
  createdAt: "2026-01-01T00:00:00Z",
};

const TEST_RESULT: PortalTestResult = {
  success: true,
  statusCode: 200,
  latencyMs: 87,
  responseBody: "ok",
  error: null,
  request: {
    url: "https://consumer.example.com/hooks",
    headers: {
      "webhook-id": "msg_abc123",
      "webhook-timestamp": "1715363400",
      "webhook-signature": "v1,abc123def456==",
      "Content-Type": "application/json",
    },
    body: '{"orderId":"abc123"}',
  },
};

function makeClient(overrides: Partial<PortalClient> = {}): PortalClient {
  return {
    listEndpoints: vi.fn(),
    getEndpoint: vi.fn(),
    createEndpoint: vi.fn(),
    updateEndpoint: vi.fn(),
    deleteEndpoint: vi.fn(),
    enableEndpoint: vi.fn(),
    disableEndpoint: vi.fn(),
    listEventTypes: vi.fn().mockResolvedValue([
      { id: "et-1", name: "order.created", description: null },
      { id: "et-2", name: "payment.failed", description: null },
    ]),
    testEndpoint: vi.fn().mockResolvedValue(TEST_RESULT),
    listAttempts: vi.fn(),
    ...overrides,
  };
}

/** Set textarea value using fireEvent to avoid user-event brace escaping issues */
function setTextarea(textarea: HTMLElement, value: string) {
  fireEvent.change(textarea, { target: { value } });
  fireEvent.blur(textarea);
}

describe("EndpointTester", () => {
  const onClose = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders the request form when endpoints:test capability is present", async () => {
    const client = makeClient();
    render(
      <EndpointTester
        client={client}
        endpoint={ENDPOINT}
        capabilities={["endpoints:read", "endpoints:test"]}
        onClose={onClose}
      />,
    );

    await waitFor(() =>
      expect(screen.getByRole("combobox")).toBeInTheDocument(),
    );
    expect(screen.getByRole("button", { name: /Send test/i })).toBeInTheDocument();
  });

  it("renders capability banner instead of form when endpoints:test is missing", async () => {
    const client = makeClient();
    render(
      <EndpointTester
        client={client}
        endpoint={ENDPOINT}
        capabilities={["endpoints:read"]}
        onClose={onClose}
      />,
    );

    // Wait for any async state from listEventTypes to settle before asserting.
    await waitFor(() => expect(client.listEventTypes).toHaveBeenCalled());

    expect(screen.getByText(/Capability required/i)).toBeInTheDocument();
    expect(screen.getByText(/endpoints:test/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Send test/i })).not.toBeInTheDocument();
    // Footer should have "Close" text (not Cancel).
    expect(screen.getByText("Close")).toBeInTheDocument();
  });

  it("invalid JSON in payload textarea surfaces inline error and disables Send test", async () => {
    const client = makeClient();
    render(
      <EndpointTester
        client={client}
        endpoint={ENDPOINT}
        capabilities={["endpoints:test"]}
        onClose={onClose}
      />,
    );

    await waitFor(() => screen.getByRole("combobox"));

    const textarea = screen.getByRole("textbox");
    setTextarea(textarea, "not valid json{");

    await waitFor(() =>
      expect(screen.getByText(/Invalid JSON/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole("button", { name: /Send test/i })).toBeDisabled();
  });

  it("submitting fires client.testEndpoint with correct body shape", async () => {
    const user = userEvent.setup();
    const client = makeClient();
    render(
      <EndpointTester
        client={client}
        endpoint={ENDPOINT}
        capabilities={["endpoints:test"]}
        onClose={onClose}
      />,
    );

    // Wait for event types to load and first option to be auto-selected.
    await waitFor(() =>
      expect((screen.getByRole("combobox") as HTMLSelectElement).value).toBe("et-1"),
    );

    // Set payload via fireEvent to avoid brace-escaping in user-event.
    const textarea = screen.getByRole("textbox");
    setTextarea(textarea, '{"orderId":"abc123"}');

    await user.click(screen.getByRole("button", { name: /Send test/i }));

    await waitFor(() => expect(client.testEndpoint).toHaveBeenCalledOnce());

    const [calledId, calledInput] = (
      client.testEndpoint as ReturnType<typeof vi.fn>
    ).mock.calls[0] as [string, { eventType: string; payload: Record<string, unknown> }];
    expect(calledId).toBe("ep-1");
    expect(calledInput.eventType).toBe("et-1");
    expect(calledInput.payload).toEqual({ orderId: "abc123" });
  });

  it("response panel renders status code, latency, and body after successful send", async () => {
    const user = userEvent.setup();
    const client = makeClient();
    render(
      <EndpointTester
        client={client}
        endpoint={ENDPOINT}
        capabilities={["endpoints:test"]}
        onClose={onClose}
      />,
    );

    await waitFor(() =>
      expect((screen.getByRole("combobox") as HTMLSelectElement).value).toBe("et-1"),
    );

    const textarea = screen.getByRole("textbox");
    setTextarea(textarea, '{"key":"val"}');

    await user.click(screen.getByRole("button", { name: /Send test/i }));

    await waitFor(() =>
      expect(screen.getByText("200")).toBeInTheDocument(),
    );
    expect(screen.getByText(/87 ms/i)).toBeInTheDocument();
    expect(screen.getByText("ok")).toBeInTheDocument();
  });

  it("signed-request preview is collapsible and shows webhook-signature header", async () => {
    const user = userEvent.setup();
    const client = makeClient();
    render(
      <EndpointTester
        client={client}
        endpoint={ENDPOINT}
        capabilities={["endpoints:test"]}
        onClose={onClose}
      />,
    );

    await waitFor(() =>
      expect((screen.getByRole("combobox") as HTMLSelectElement).value).toBe("et-1"),
    );

    const textarea = screen.getByRole("textbox");
    setTextarea(textarea, '{"k":"v"}');

    await user.click(screen.getByRole("button", { name: /Send test/i }));

    // Wait for result panel to appear.
    await waitFor(() => screen.getByText("200"));

    // Expand the signed-request preview.
    const previewBtn = screen.getByRole("button", { name: /Signed request preview/i });
    await user.click(previewBtn);

    await waitFor(() =>
      expect(screen.getByText(/webhook-signature/i)).toBeInTheDocument(),
    );
    expect(screen.getByText(/v1,abc123def456/i)).toBeInTheDocument();
  });

  it("Cancel button in footer calls onClose", async () => {
    const user = userEvent.setup();
    render(
      <EndpointTester
        client={makeClient()}
        endpoint={ENDPOINT}
        capabilities={["endpoints:test"]}
        onClose={onClose}
      />,
    );

    await waitFor(() => screen.getByRole("combobox"));
    // Footer cancel button (has text "Cancel", not aria-label "Close").
    await user.click(screen.getByRole("button", { name: /^Cancel$/i }));
    expect(onClose).toHaveBeenCalledOnce();
  });
});
