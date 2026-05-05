import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { getCurrentUser, login as apiLogin, logout as apiLogout, type AuthUser } from "../api/authApi";
import { AuthEvents } from "../api/dashboardApi";

interface AuthContextValue {
  user: AuthUser | null;
  isLoading: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    let isMounted = true;

    async function init() {
      try {
        const me = await getCurrentUser();
        if (!isMounted) {
          return;
        }
        setUser(me);
      } finally {
        if (isMounted) {
          setIsLoading(false);
        }
      }
    }

    void init();

    // Listen for the global auth-expired signal that dashboardApi raises on
    // any 401 response. Clears the user so ProtectedRoute bounces to /login.
    const onExpired = () => {
      if (!isMounted) return;
      setUser(null);
      setIsLoading(false);
    };
    window.addEventListener(AuthEvents.AuthExpired, onExpired);

    return () => {
      isMounted = false;
      window.removeEventListener(AuthEvents.AuthExpired, onExpired);
    };
  }, []);

  const login = useCallback(async (email: string, password: string) => {
    const authUser = await apiLogin(email, password);
    setUser(authUser);
  }, []);

  const logout = useCallback(async () => {
    await apiLogout();
    setUser(null);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      isLoading,
      login,
      logout
    }),
    [isLoading, login, logout, user]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within AuthProvider");
  }
  return context;
}
