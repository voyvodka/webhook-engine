import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { AuthProvider } from "./auth/AuthContext";
import { AppShell } from "./layout/AppShell";
import { ApplicationsPage } from "./pages/ApplicationsPage";
import { DashboardPage } from "./pages/DashboardPage";
import { DeliveryLogPage } from "./pages/DeliveryLogPage";
import { EndpointsPage } from "./pages/EndpointsPage";
import { LoginPage } from "./pages/LoginPage";
import { MessagesPage } from "./pages/MessagesPage";
import { ProtectedRoute } from "./routes/ProtectedRoute";

export function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />

          <Route element={<ProtectedRoute />}>
            <Route element={<AppShell />}>
              <Route path="/" element={<DashboardPage />} />
              <Route path="/applications" element={<ApplicationsPage />} />
              <Route path="/endpoints" element={<EndpointsPage />} />
              <Route path="/messages" element={<MessagesPage />} />
              <Route path="/delivery-log" element={<DeliveryLogPage />} />
              <Route path="/delivery-log/:messageId" element={<DeliveryLogPage />} />
            </Route>
          </Route>

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
