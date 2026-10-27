import { useState, type FormEvent } from "react";
import { api } from "../api";
import { ErrorAlert, useUser } from "../components";
import { roleLabel } from "../labels";
import type { CurrentUser } from "../types";

const MIN_LENGTH = 10;

/** Change password. With `required`, the account holds a temporary password and can do nothing else until it is replaced. */
export default function AccountPage({ required = false, onChanged }: { required?: boolean; onChanged: (user: CurrentUser) => void }) {
  const user = useUser();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  const mismatch = confirm.length > 0 && next !== confirm;
  const canSave = current && next.length >= MIN_LENGTH && next === confirm && !busy;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    setDone(false);
    try {
      const updated = await api.post<CurrentUser>("/api/auth/change-password", { currentPassword: current, newPassword: next });
      setCurrent(""); setNext(""); setConfirm("");
      setDone(true);
      onChanged(updated);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>{required ? "Choose a new password" : "Your account"}</h1>
          <p className="muted">
            {required
              ? "You signed in with a temporary password. Choose your own before you continue."
              : user && <>{user.displayName} · {roleLabel[user.role]}{user.email ? ` · ${user.email}` : ""}</>}
          </p>
        </div>
      </div>
      <form className="card stack account-form" onSubmit={submit} noValidate>
        <h2>Change password</h2>
        <ErrorAlert error={error} />
        {done && <div className="alert info" role="status">Password changed. Your other sessions have been signed out.</div>}
        <label className="field">
          Current password
          <input id="current-password" type="password" value={current} onChange={(e) => setCurrent(e.target.value)}
            autoComplete="current-password" required />
        </label>
        <label className="field">
          New password
          <input id="new-password" type="password" value={next} onChange={(e) => setNext(e.target.value)}
            autoComplete="new-password" aria-describedby="password-rules" required />
        </label>
        <p id="password-rules" className="muted small">
          At least {MIN_LENGTH} characters, with a letter and a digit or symbol, and not containing your username.
          A short phrase is easier to remember than a jumble of symbols.
        </p>
        <label className="field">
          Confirm new password
          <input id="confirm-password" type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)}
            autoComplete="new-password" aria-invalid={mismatch} required />
        </label>
        {mismatch && <p className="field-error small">The two new passwords do not match.</p>}
        <div className="row">
          <button className="primary" type="submit" disabled={!canSave}>{busy ? "Saving…" : "Change password"}</button>
        </div>
      </form>
    </>
  );
}
