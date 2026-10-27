import { useState, type FormEvent } from "react";
import { api } from "../api";
import { Badge, ErrorAlert, useLoad } from "../components";
import { roleLabel } from "../labels";
import type { CurrentUser, DemoAccount } from "../types";

export default function LoginPage({ onSignedIn, notice }: { onSignedIn: (user: CurrentUser) => void; notice?: string | null }) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const options = useLoad<{ demoAccounts: DemoAccount[] }>("/api/auth/options");
  const demo = options.data?.demoAccounts ?? [];

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      onSignedIn(await api.post<CurrentUser>("/api/auth/login", { username, password }));
    } catch (err) {
      setError((err as Error).message);
      setPassword("");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="auth-wrap">
      <form className="card auth-card stack" onSubmit={submit} noValidate>
        <div>
          <h1>Sign in</h1>
          <p className="muted">Use your Transfer Credit Review account. What you can see and do depends on your role.</p>
        </div>
        {notice && !error && <div className="alert info">{notice}</div>}
        <ErrorAlert error={error} />
        <label className="field">
          Username or email
          <input id="login-username" value={username} onChange={(e) => setUsername(e.target.value)}
            autoComplete="username" autoCapitalize="none" spellCheck={false} autoFocus required />
        </label>
        <div className="field">
          <label htmlFor="login-password">Password</label>
          <span className="password-row">
            <input id="login-password" type={showPassword ? "text" : "password"} value={password}
              onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required />
            <button type="button" onClick={() => setShowPassword((s) => !s)} aria-pressed={showPassword}
              aria-label={showPassword ? "Hide password" : "Show password"}>{showPassword ? "Hide" : "Show"}</button>
          </span>
        </div>
        <button className="primary" type="submit" disabled={busy || !username || !password}>
          {busy ? "Signing in…" : "Sign in"}
        </button>
        <p className="muted small">Forgot your password? Ask an administrator to reset it.</p>
      </form>

      {demo.length > 0 && (
        <section className="card auth-card stack" aria-labelledby="demo-accounts">
          <div>
            <h2 id="demo-accounts">Demo accounts</h2>
            <p className="muted small">
              Shown in development only. Every demo account uses the password set in <span className="mono">Auth:SeedPassword</span>.
              Choose one to fill in the username.
            </p>
          </div>
          <ul className="demo-accounts">
            {demo.map((a) => (
              <li key={a.username}>
                <button type="button" onClick={() => { setUsername(a.username); document.getElementById("login-password")?.focus(); }}>
                  <span className="mono">{a.username}</span>
                  <span className="muted small">{a.displayName}</span>
                  <Badge tone="neutral">{roleLabel[a.role]}</Badge>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
