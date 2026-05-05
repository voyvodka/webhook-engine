import { lazy, Suspense } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
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

export function App() {
  return (
    <RouteErrorBoundary>
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
    </RouteErrorBoundary>
  );
}
