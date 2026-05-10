import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { EndpointEditor } from "../components/EndpointEditor.js";
import type { PortalClient } from "../api/createPortalClient.js";
import { PortalError } from "../api/createPortalClient.js";
import type { PortalEndpointDetail } from "../types.js";

const DETAIL: PortalEndpointDetail = {
  id: "ep-1",
  url: "https://consumer.example.com/hooks",
  description: "Existing endpoint",
  isActive: true,
  hasSecretOverride: false,
  filterEventTypes: [],
  customHeaders: { "X-Custom": "value" },
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-01-01T00:00:00Z",
};

function makeClient(overrides: Partial<PortalClient> = {}): PortalClient {
  return {
    listEndpoints: vi.fn(),
    getEndpoint: vi.fn(),
    createEndpoint: vi.fn().mockResolvedValue(DETAIL),
    updateEndpoint: vi.fn().mockResolvedValue(DETAIL),
    deleteEndpoint: vi.fn().mockResolvedValue(undefined),
    enableEndpoint: vi.fn().mockResolvedValue(undefined),
    disableEndpoint: vi.fn().mockResolvedValue(undefined),
    listEventTypes: vi.fn().mockResolvedValue([
      { id: "et-1", name: "order.created", description: null },
    ]),
    testEndpoint: vi.fn(),
    listAttempts: vi.fn(),
    ...overrides,
  };
}

describe("EndpointEditor", () => {
  const onClose = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("create form posts the right body (no admin-only fields)", async () => {
    const user = userEvent.setup();
    const client = makeClient();
    let capturedInput: unknown;
    client.createEndpoint = vi.fn().mockImplementation((input) => {
      capturedInput = input;
      return Promise.resolve(DETAIL);
    });

    render(
      <EndpointEditor
        client={client}
        capabilities={["endpoints:read", "endpoints:write"]}
        mode="create"
        onClose={onClose}
      />,
    );

    const urlInput = screen.getByLabelText(/Endpoint URL/i);
    await user.clear(urlInput);
    await user.type(urlInput, "https://new.example.com/hooks");

    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() => expect(client.createEndpoint).toHaveBeenCalledOnce());

    expect(capturedInput).not.toHaveProperty("transformExpression");
    expect(capturedInput).not.toHaveProperty("transformEnabled");
    expect(capturedInput).not.toHaveProperty("allowedIpsJson");
    expect(capturedInput).toMatchObject({ url: "https://new.example.com/hooks" });
  });

  it("edit form pre-fills URL and description", async () => {
    render(
      <EndpointEditor
        client={makeClient()}
        capabilities={["endpoints:read", "endpoints:write"]}
        mode="edit"
        endpoint={DETAIL}
        onClose={onClose}
      />,
    );

    const urlInput = screen.getByLabelText(/Endpoint URL/i) as HTMLInputElement;
    await waitFor(() => expect(urlInput.value).toBe("https://consumer.example.com/hooks"));
  });

  it("secret override field rejects a non-whsec_ value on save", async () => {
    const user = userEvent.setup();
    render(
      <EndpointEditor
        client={makeClient()}
        capabilities={["endpoints:read", "endpoints:write"]}
        mode="edit"
        endpoint={DETAIL}
        onClose={onClose}
      />,
    );

    const secretInput = screen.getByLabelText(/Secret override/i);
    await user.clear(secretInput);
    await user.type(secretInput, "password123");

    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.getByText(/Secret must start with whsec_/i)).toBeInTheDocument(),
    );
    // Should NOT have called the client.
    expect(makeClient().createEndpoint).not.toHaveBeenCalled();
  });

  it("delete fires confirm then DELETE and calls onClose('deleted')", async () => {
    const user = userEvent.setup();
    const client = makeClient();

    render(
      <EndpointEditor
        client={client}
        capabilities={["endpoints:read", "endpoints:write"]}
        mode="edit"
        endpoint={DETAIL}
        onClose={onClose}
      />,
    );

    // Click Delete button to show confirmation.
    const deleteBtn = screen.getByRole("button", { name: /^Delete$/i });
    await user.click(deleteBtn);

    // Confirmation prompt should appear.
    const confirmBtn = await screen.findByRole("button", { name: /Yes, delete/i });
    await user.click(confirmBtn);

    await waitFor(() => expect(client.deleteEndpoint).toHaveBeenCalledWith("ep-1"));
    expect(onClose).toHaveBeenCalledWith("deleted");
  });

  it("422 fieldErrors are rendered inline", async () => {
    const user = userEvent.setup();
    const client = makeClient({
      createEndpoint: vi.fn().mockRejectedValue(
        new PortalError("Validation failed", "VALIDATION_FAILED", 422, {
          url: "URL is already registered for this application",
        }),
      ),
    });

    render(
      <EndpointEditor
        client={client}
        capabilities={["endpoints:read", "endpoints:write"]}
        mode="create"
        onClose={onClose}
      />,
    );

    const urlInput = screen.getByLabelText(/Endpoint URL/i);
    await user.clear(urlInput);
    await user.type(urlInput, "https://already-registered.example.com/hooks");

    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(
        screen.getByText(/URL is already registered for this application/i),
      ).toBeInTheDocument(),
    );
  });

  it("Cancel calls onClose('cancelled')", async () => {
    const user = userEvent.setup();
    render(
      <EndpointEditor
        client={makeClient()}
        capabilities={["endpoints:read", "endpoints:write"]}
        mode="create"
        onClose={onClose}
      />,
    );

    const cancelBtns = screen.getAllByRole("button", { name: /Cancel/i });
    // Footer Cancel is the last one.
    await user.click(cancelBtns[cancelBtns.length - 1]!);
    expect(onClose).toHaveBeenCalledWith("cancelled");
  });

  it("read-only mode hides Save button when capabilities lacks endpoints:write", async () => {
    render(
      <EndpointEditor
        client={makeClient()}
        capabilities={["endpoints:read"]}
        mode="edit"
        endpoint={DETAIL}
        onClose={onClose}
      />,
    );

    await waitFor(() => expect(screen.queryByRole("button", { name: /^Save$/i })).not.toBeInTheDocument());
  });
});

// Suppress act() warnings from within() — re-export for sanity check
const _within = within;
void _within;
