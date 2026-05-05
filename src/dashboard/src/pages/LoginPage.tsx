import { FormEvent, useEffect, useState } from "react";
import { useLocation, useNavigate } from "react-router";
import { useAuth } from "../auth/AuthContext";
import { Logo } from "../components/Logo";
import { ArrowRight, AlertCircle, Loader2 } from "lucide-react";

interface LocationState {
  from?: string;
}

export function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { user, login } = useAuth();

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const from = (location.state as LocationState | null)?.from ?? "/";

  useEffect(() => {
    if (user) {
      navigate(from, { replace: true });
    }
  }, [from, navigate, user]);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    setIsSubmitting(true);

    const minDelay = new Promise((r) => setTimeout(r, 600));

    try {
      const [result] = await Promise.allSettled([login(email, password), minDelay]);
      if (result.status === "rejected") throw result.reason;
      navigate(from, { replace: true });
    } catch (err) {
      await minDelay;
      setError(err instanceof Error ? err.message : "Login failed");
      setIsSubmitting(false);
    }
  };

  return (
    <div className="h-full flex items-center justify-center bg-surface-0 px-4">
      {/* Subtle grid background */}
      <div className="fixed inset-0 bg-[linear-gradient(rgba(34,211,238,0.03)_1px,transparent_1px),linear-gradient(90deg,rgba(34,211,238,0.03)_1px,transparent_1px)] bg-[size:64px_64px]" />

      <div className="relative w-full max-w-sm animate-fade-in-up">
        {/* Brand */}
        <div className="flex items-center gap-2.5 mb-8">
          <Logo size={32} />
          <span className="text-lg font-semibold tracking-tight">
            Webhook<span className="text-accent">Engine</span>
          </span>
        </div>

        {/* Card */}
        <div className="rounded-xl border border-border bg-surface-1 p-6">
          <div className="mb-5">
            <h1 className="text-xl font-semibold mb-1">Sign in</h1>
            <p className="text-sm text-text-muted">Access your webhook operations dashboard.</p>
          </div>

          <form onSubmit={handleSubmit} className="space-y-4">
            <label className="block">
              <span className="text-xs font-medium text-text-secondary mb-1.5 block">Email</span>
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                autoComplete="username"
                placeholder="admin@example.com"
                required
                className="w-full px-3 py-2 text-sm bg-surface-2 border border-border rounded-lg text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-1 focus:ring-accent/50 focus:border-accent/50 transition-colors"
              />
            </label>

            <label className="block">
              <span className="text-xs font-medium text-text-secondary mb-1.5 block">Password</span>
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
                placeholder="••••••••"
                required
                className="w-full px-3 py-2 text-sm font-mono bg-surface-2 border border-border rounded-lg text-text-primary placeholder:text-text-muted focus:outline-none focus:ring-1 focus:ring-accent/50 focus:border-accent/50 transition-colors"
              />
            </label>

            <div
              className={`overflow-hidden transition-all duration-200 ${error ? "max-h-12 opacity-100" : "max-h-0 opacity-0"}`}
            >
              <div className="flex items-center gap-2 text-danger text-xs bg-danger-soft rounded-lg px-3 py-2">
                <AlertCircle className="w-3.5 h-3.5 shrink-0" />
                {error}
              </div>
            </div>

            <button
              type="submit"
              disabled={isSubmitting}
              className="w-full flex items-center justify-center gap-2 px-4 py-2 text-sm font-medium rounded-lg bg-accent text-zinc-950 hover:bg-accent/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            >
              {isSubmitting ? (
                <>
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  <span>Signing in...</span>
                </>
              ) : (
                <>
                  <span>Sign in</span>
                  <ArrowRight className="w-3.5 h-3.5" />
                </>
              )}
            </button>
          </form>
        </div>

        <p className="text-xs text-text-muted mt-4 text-center">
          Self-hosted webhook delivery platform
        </p>
      </div>
    </div>
  );
}
