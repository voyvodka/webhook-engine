import { Component, type ErrorInfo, type ReactNode } from "react";

interface State {
  error: Error | null;
}

/**
 * Catches anything thrown inside the lazy-loaded routes — most commonly
 * `ChunkLoadError` after a deploy that retired the old asset hashes.
 * Without this, React unmounts the tree and the user sees a frozen
 * Suspense fallback or a blank screen until they refresh.
 */
export class RouteErrorBoundary extends Component<{ children: ReactNode }, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Surface to console for diagnosis; in production the SPA host also
    // tails server-side logs so duplicate sinking is fine.
    console.error("RouteErrorBoundary caught:", error, info);
  }

  private handleReload = () => window.location.reload();

  render() {
    if (this.state.error) {
      const isChunkError =
        /loading chunk|loading css chunk|importing.*module/i.test(this.state.error.message);

      return (
        <div className="h-screen flex items-center justify-center bg-surface-0 text-text-primary">
          <div className="max-w-md text-center space-y-3">
            <h1 className="text-lg font-semibold">
              {isChunkError ? "Update available" : "Something went wrong"}
            </h1>
            <p className="text-sm text-text-secondary">
              {isChunkError
                ? "The dashboard was updated. Reload to fetch the latest version."
                : this.state.error.message}
            </p>
            <button
              onClick={this.handleReload}
              className="text-xs font-medium px-3 py-1.5 rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 transition-colors"
            >
              Reload
            </button>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
