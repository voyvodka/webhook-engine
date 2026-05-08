import { lazy, Suspense } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuthProvider } from "./auth/AuthContext";
import { AppShell } from "./layout/AppShell";
import { LoginPage } from "./pages/LoginPage";
import { ProtectedRoute } from "./routes/ProtectedRoute";
import { RouteErrorBoundary } from "./components/RouteErrorBoundary";

const DashboardPage = lazy(() =>
  import("./pages/DashboardPage").then((m) => ({ default: m.DashboardPage }))
);
const ApplicationsPage = lazy(() =>
  import("./pages/ApplicationsPage").then((m) => ({ default: m.ApplicationsPage }))
);
const EventTypesPage = lazy(() =>
  import("./pages/EventTypesPage").then((m) => ({ default: m.EventTypesPage }))
);
const EndpointsPage = lazy(() =>
  import("./pages/EndpointsPage").then((m) => ({ default: m.EndpointsPage }))
);
const MessagesPage = lazy(() =>
  import("./pages/MessagesPage").then((m) => ({ default: m.MessagesPage }))
);
const DeliveryLogPage = lazy(() =>
  import("./pages/DeliveryLogPage").then((m) => ({ default: m.DeliveryLogPage }))
);

function PageFallback() {
  return (
    <div className="h-64 flex items-center justify-center text-text-muted text-xs">
      Loading…
    </div>
  );
}

// Single QueryClient for the whole dashboard. Defaults are tuned for an
// authenticated long-lived dashboard, not a public site:
// - staleTime 30 s: the SignalR feed is the live channel; HTTP fetches just
//   refresh the static aggregates, so a 30-second stale window keeps the
//   network cost down without hiding meaningful changes.
// - retry 1: dashboard requests are idempotent reads; one transient retry is
//   useful, but stacking five buys nothing the SignalR layer doesn't already.
// - refetchOnWindowFocus false: a long-running dashboard otherwise fires a
//   storm of refetches every time the user alt-tabs back.
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      refetchOnWindowFocus: false
    }
  }
});

export function App() {
  return (
    <RouteErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <BrowserRouter>
          <Routes>
          <Route path="/login" element={<LoginPage />} />

          <Route element={<ProtectedRoute />}>
            <Route element={<AppShell />}>
              <Route
                path="/"
                element={
                  <Suspense fallback={<PageFallback />}>
                    <DashboardPage />
                  </Suspense>
                }
              />
              <Route
                path="/applications"
                element={
                  <Suspense fallback={<PageFallback />}>
                    <ApplicationsPage />
                  </Suspense>
                }
              />
              <Route
                path="/event-types"
                element={
                  <Suspense fallback={<PageFallback />}>
                    <EventTypesPage />
                  </Suspense>
                }
              />
              <Route
                path="/endpoints"
                element={
                  <Suspense fallback={<PageFallback />}>
                    <EndpointsPage />
                  </Suspense>
                }
              />
              <Route
                path="/messages"
                element={
                  <Suspense fallback={<PageFallback />}>
                    <MessagesPage />
                  </Suspense>
                }
              />
              <Route
                path="/delivery-log/:messageId"
                element={
                  <Suspense fallback={<PageFallback />}>
                    <DeliveryLogPage />
                  </Suspense>
                }
              />
            </Route>
          </Route>

            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </BrowserRouter>
        </AuthProvider>
      </QueryClientProvider>
    </RouteErrorBoundary>
  );
}
