import { NavLink, Outlet, useNavigate } from "react-router";
import { useAuth } from "../auth/AuthContext";
import {
  LayoutDashboard,
  Box,
  Tags,
  Waypoints,
  Mail,
  FileText,
  LogOut,
  Webhook,
  ChevronRight
} from "lucide-react";

const navItems = [
  { to: "/", label: "Overview", icon: LayoutDashboard, end: true },
  { to: "/applications", label: "Applications", icon: Box },
  { to: "/event-types", label: "Event Types", icon: Tags },
  { to: "/endpoints", label: "Endpoints", icon: Waypoints },
  { to: "/messages", label: "Messages", icon: Mail },
  { to: "/delivery-log", label: "Delivery Logs", icon: FileText }
];

export function AppShell() {
  const navigate = useNavigate();
  const { user, logout } = useAuth();

  const handleLogout = async () => {
    await logout();
    navigate("/login", { replace: true });
  };

  return (
    <div className="flex h-full">
      {/* Sidebar */}
      <aside className="w-56 shrink-0 flex flex-col border-r border-border bg-surface-1">
        {/* Brand */}
        <div className="px-4 pt-5 pb-4 border-b border-border-subtle">
          <div className="flex items-center gap-2">
            <div className="w-7 h-7 rounded-md bg-accent/10 flex items-center justify-center">
              <Webhook className="w-4 h-4 text-accent" />
            </div>
            <div>
              <span className="text-sm font-semibold text-text-primary tracking-tight">
                WebhookEngine
              </span>
            </div>
          </div>
        </div>

        {/* Nav */}
        <nav className="flex-1 px-2 py-3 space-y-0.5 overflow-y-auto">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) =>
                `group flex items-center gap-2.5 px-2.5 py-1.5 rounded-md text-[13px] transition-colors duration-100 ${
                  isActive
                    ? "bg-accent-soft text-accent font-medium"
                    : "text-text-secondary hover:text-text-primary hover:bg-surface-2"
                }`
              }
            >
              {({ isActive }) => (
                <>
                  <item.icon className={`w-4 h-4 shrink-0 ${isActive ? "text-accent" : "text-text-muted group-hover:text-text-secondary"}`} />
                  <span className="flex-1">{item.label}</span>
                  {isActive && <ChevronRight className="w-3 h-3 text-accent/50" />}
                </>
              )}
            </NavLink>
          ))}
        </nav>

        {/* Footer */}
        <div className="px-3 py-3 border-t border-border-subtle">
          <div className="flex items-center justify-between gap-2">
            <div className="min-w-0">
              <p className="text-xs text-text-muted truncate font-mono">
                {user?.email ?? "anonymous"}
              </p>
            </div>
            <button
              onClick={handleLogout}
              className="p-1.5 rounded-md text-text-muted hover:text-danger hover:bg-danger-soft transition-colors"
              title="Sign out"
            >
              <LogOut className="w-3.5 h-3.5" />
            </button>
          </div>
        </div>
      </aside>

      {/* Main */}
      <main className="flex-1 min-w-0 overflow-y-auto bg-surface-0">
        <div className="max-w-7xl mx-auto px-5 py-5">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
