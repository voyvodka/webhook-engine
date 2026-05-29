import { useMemo, useReducer, useCallback } from "react";
import type { JSX } from "react";
import type { EndpointManagerProps, PortalCapability, PortalEndpointSummary, PortalEndpointDetail } from "./types.js";
import { createPortalClient } from "./api/createPortalClient.js";
import { EndpointList } from "./components/EndpointList.js";
import { EndpointEditor } from "./components/EndpointEditor.js";
import type { EditorMode } from "./components/EndpointEditor.js";

interface ManagerState {
  view: "list" | "editor";
  editorMode: EditorMode;
  selectedEndpoint: PortalEndpointDetail | null;
  /** Bump this to force the list to refetch. */
  listKey: number;
}

type ManagerAction =
  | { type: "OPEN_NEW" }
  | { type: "OPEN_EDIT"; endpoint: PortalEndpointDetail }
  | { type: "CLOSE_EDITOR"; action: "saved" | "deleted" | "cancelled" };

function managerReducer(state: ManagerState, action: ManagerAction): ManagerState {
  switch (action.type) {
    case "OPEN_NEW":
      return { ...state, view: "editor", editorMode: "create", selectedEndpoint: null };
    case "OPEN_EDIT":
      return {
        ...state,
        view: "editor",
        editorMode: "edit",
        selectedEndpoint: action.endpoint,
      };
    case "CLOSE_EDITOR":
      return {
        ...state,
        view: "list",
        selectedEndpoint: null,
        // Bump listKey so the list re-fetches after a save or delete.
        listKey: action.action !== "cancelled" ? state.listKey + 1 : state.listKey,
      };
  }
}

const DEFAULT_CAPABILITIES: PortalCapability[] = ["endpoints:read"];

export function EndpointManager(props: EndpointManagerProps): JSX.Element {
  const {
    baseUrl,
    token,
    appId,
    capabilities = DEFAULT_CAPABILITIES,
    className,
    onError,
    onUnauthorized,
  } = props;

  const client = useMemo(
    () =>
      createPortalClient({
        baseUrl,
        token,
        onError,
        onUnauthorized,
      }),
    [baseUrl, token, onError, onUnauthorized],
  );

  const [state, dispatch] = useReducer(managerReducer, {
    view: "list",
    editorMode: "create",
    selectedEndpoint: null,
    listKey: 0,
  });

  const handleEditEndpoint = useCallback(
    async (summary: PortalEndpointSummary) => {
      try {
        const detail = await client.getEndpoint(summary.id);
        dispatch({ type: "OPEN_EDIT", endpoint: detail });
      } catch {
        // Fall back to a partial detail from the summary if getEndpoint fails.
        const fallback: PortalEndpointDetail = {
          id: summary.id,
          url: summary.url,
          description: summary.description,
          status: summary.status,
          hasSecretOverride: summary.hasSecretOverride,
          filterEventTypes: summary.filterEventTypes,
          customHeaderNames: [],
          createdAt: summary.createdAt,
          updatedAt: summary.createdAt,
        };
        dispatch({ type: "OPEN_EDIT", endpoint: fallback });
      }
    },
    [client],
  );

  const handleCloseEditor = useCallback(
    (action: "saved" | "deleted" | "cancelled") => {
      dispatch({ type: "CLOSE_EDITOR", action });
    },
    [],
  );

  return (
    <div className={`whe-portal ${className ?? ""}`}>
      {state.view === "list" && (
        <EndpointList
          key={state.listKey}
          client={client}
          appId={appId}
          capabilities={capabilities}
          onEditEndpoint={(summary) => void handleEditEndpoint(summary)}
          onNewEndpoint={() => dispatch({ type: "OPEN_NEW" })}
        />
      )}

      {state.view === "editor" && (
        <EndpointEditor
          client={client}
          capabilities={capabilities}
          mode={state.editorMode}
          endpoint={state.selectedEndpoint ?? undefined}
          onClose={handleCloseEditor}
        />
      )}
    </div>
  );
}
