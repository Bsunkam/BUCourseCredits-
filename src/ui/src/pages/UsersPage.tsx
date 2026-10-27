import { Fragment, useState, type FormEvent } from "react";
import { api } from "../api";
import { Badge, ErrorAlert, Loading, useLoad, useUser } from "../components";
import { formatDateTime, roleLabel } from "../labels";
import type { UserAccount, UserRole } from "../types";

const ROLES: UserRole[] = ["Staff", "Evaluator", "Admin", "Advisor", "Student"];

/** A random temporary password: 14 characters, never ambiguous ones like 0/O or 1/l, always with a digit. */
function temporaryPassword(): string {
  const letters = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
  const digits = "23456789";
  const all = letters + digits + "-#";
  const bytes = crypto.getRandomValues(new Uint32Array(14));
  const chars = Array.from(bytes, (b) => all[b % all.length]);
  chars[3] = digits[bytes[3] % digits.length];
  chars[9] = letters[bytes[9] % letters.length];
  return chars.join("");
}

interface Draft { id: string; displayName: string; role: UserRole; email: string; studentRef: string; isActive: boolean; password: string }
const emptyDraft: Draft = { id: "", displayName: "", role: "Staff", email: "", studentRef: "", isActive: true, password: "" };

type Panel = { kind: "create" } | { kind: "edit"; user: UserAccount } | { kind: "reset"; user: UserAccount } | null;

export default function UsersPage() {
  const me = useUser();
  const { data, error, reload, setError } = useLoad<UserAccount[]>("/api/admin/users");
  const [panel, setPanel] = useState<Panel>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [q, setQ] = useState("");

  const rows = (data ?? []).filter((u) => {
    const s = q.trim().toLowerCase();
    return !s || [u.id, u.displayName, u.email ?? "", u.studentRef ?? "", roleLabel[u.role]].some((v) => v.toLowerCase().includes(s));
  });

  async function act(fn: () => Promise<unknown>, message: string) {
    try {
      await fn();
      setNotice(message);
      setError(null);
      await reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Users & roles</h1>
          <p className="muted">Who can sign in, and what each role may do. Changing a role or disabling an account signs that person out at once.</p>
        </div>
        <button className="primary" onClick={() => { setPanel({ kind: "create" }); setNotice(null); }}>New user</button>
      </div>

      <div className="stack">
        {notice && <div className="alert info" role="status">{notice}</div>}
        <ErrorAlert error={error} />

        {panel?.kind === "create" && (
          <UserForm title="New user" initial={{ ...emptyDraft, password: temporaryPassword() }} isNew onCancel={() => setPanel(null)}
            onSave={(d) => act(async () => {
              await api.post("/api/admin/users", {
                id: d.id, displayName: d.displayName, role: d.role, email: d.email || null,
                studentRef: d.role === "Student" ? d.studentRef : null, password: d.password,
              });
              setPanel(null);
            }, `Account ${d.id.trim().toLowerCase()} created. Give them the temporary password; they choose their own at first sign-in.`)} />
        )}

        {panel?.kind === "edit" && (
          <UserForm title={`Edit ${panel.user.id}`} isSelf={panel.user.id === me?.id} onCancel={() => setPanel(null)}
            initial={{ ...emptyDraft, ...panel.user, email: panel.user.email ?? "", studentRef: panel.user.studentRef ?? "" }}
            onSave={(d) => act(async () => {
              await api.put(`/api/admin/users/${encodeURIComponent(panel.user.id)}`, {
                displayName: d.displayName, role: d.role, email: d.email || null,
                studentRef: d.role === "Student" ? d.studentRef : null, isActive: d.isActive,
              });
              setPanel(null);
            }, `Account ${panel.user.id} saved.`)} />
        )}

        {panel?.kind === "reset" && (
          <ResetForm user={panel.user} onCancel={() => setPanel(null)}
            onSave={(password) => act(async () => {
              await api.post(`/api/admin/users/${encodeURIComponent(panel.user.id)}/reset-password`, { newPassword: password });
              setPanel(null);
            }, `Password for ${panel.user.id} reset. They are signed out and must choose a new password at next sign-in.`)} />
        )}

        <section className="card stack">
          <div className="row between">
            <h2>Accounts{data ? ` · ${data.length}` : ""}</h2>
            <input placeholder="Search name, username, email" value={q} onChange={(e) => setQ(e.target.value)} aria-label="Search users" />
          </div>
          {!data ? <Loading /> : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr><th>User</th><th>Role</th><th>Email</th><th>Student ref</th><th>Status</th><th>Last sign-in</th><th /></tr>
                </thead>
                <tbody>
                  {rows.map((u) => (
                    <tr key={u.id}>
                      <td>{u.displayName}<div className="mono muted">{u.id}{u.id === me?.id ? " (you)" : ""}</div></td>
                      <td>{roleLabel[u.role]}</td>
                      <td className="small">{u.email ?? <span className="muted">None</span>}</td>
                      <td className="mono">{u.studentRef ?? ""}</td>
                      <td>
                        <div className="row">
                          {u.isActive ? <Badge tone="success">Active</Badge> : <Badge tone="neutral">Disabled</Badge>}
                          {u.isLocked && <Badge tone="danger">Locked</Badge>}
                          {!u.hasPassword && <Badge tone="warn">No password</Badge>}
                          {u.hasPassword && u.mustChangePassword && <Badge tone="info">Temporary password</Badge>}
                        </div>
                      </td>
                      <td className="small">{u.lastSignInAt ? formatDateTime(u.lastSignInAt) : <span className="muted">Never</span>}</td>
                      <td>
                        <div className="row row-end">
                          <button onClick={() => { setPanel({ kind: "edit", user: u }); setNotice(null); }}>Edit</button>
                          {u.id !== me?.id && (
                            <button onClick={() => { setPanel({ kind: "reset", user: u }); setNotice(null); }}>Reset password</button>
                          )}
                          {u.isLocked && (
                            <button onClick={() => act(() => api.post(`/api/admin/users/${encodeURIComponent(u.id)}/unlock`), `Account ${u.id} unlocked.`)}>
                              Unlock
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                  {rows.length === 0 && <tr><td colSpan={7} className="empty">No accounts match “{q}”.</td></tr>}
                </tbody>
              </table>
            </div>
          )}
        </section>

        <RoleGuide />
      </div>
    </>
  );
}

function UserForm({ title, initial, isNew = false, isSelf = false, onSave, onCancel }: {
  title: string; initial: Draft; isNew?: boolean; isSelf?: boolean; onSave: (d: Draft) => void; onCancel: () => void;
}) {
  const [d, setD] = useState<Draft>(initial);
  const set = <K extends keyof Draft>(k: K, v: Draft[K]) => setD((x) => ({ ...x, [k]: v }));
  const submit = (e: FormEvent) => { e.preventDefault(); onSave(d); };

  return (
    <form className="card stack" onSubmit={submit} aria-label={title}>
      <h2>{title}</h2>
      <div className="form-grid">
        {isNew && (
          <label className="field">
            Username
            <input id="user-id" value={d.id} onChange={(e) => set("id", e.target.value)} autoCapitalize="none" spellCheck={false}
              placeholder="e.g. eval-math" required />
          </label>
        )}
        <label className="field">
          Display name
          <input id="user-name" value={d.displayName} onChange={(e) => set("displayName", e.target.value)} required />
        </label>
        <label className="field">
          Role
          <select id="user-role" value={d.role} onChange={(e) => set("role", e.target.value as UserRole)} disabled={isSelf}>
            {ROLES.map((r) => <option key={r} value={r}>{roleLabel[r]}</option>)}
          </select>
        </label>
        <label className="field">
          Email
          <input id="user-email" type="email" value={d.email} onChange={(e) => set("email", e.target.value)} />
        </label>
        {d.role === "Student" && (
          <label className="field">
            Student reference
            <input id="user-student-ref" value={d.studentRef} onChange={(e) => set("studentRef", e.target.value)} placeholder="DS-0003" required />
          </label>
        )}
        {isNew && (
          <div className="field">
            <label htmlFor="user-password">Temporary password</label>
            <span className="password-row">
              <input id="user-password" value={d.password} onChange={(e) => set("password", e.target.value)} className="mono" required />
              <button type="button" onClick={() => set("password", temporaryPassword())} aria-label="Generate a new temporary password">New</button>
            </span>
          </div>
        )}
      </div>
      {!isNew && (
        <label className="row check">
          <input id="user-active" type="checkbox" checked={d.isActive} onChange={(e) => set("isActive", e.target.checked)} disabled={isSelf} />
          Account can sign in
        </label>
      )}
      {isSelf && <p className="muted small">You cannot change your own role or disable your own account.</p>}
      {d.role === "Student" && (
        <p className="muted small">A student sees only the cases whose student reference matches this one.</p>
      )}
      <div className="row">
        <button className="primary" type="submit">{isNew ? "Create account" : "Save"}</button>
        <button type="button" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}

function ResetForm({ user, onSave, onCancel }: { user: UserAccount; onSave: (password: string) => void; onCancel: () => void }) {
  const [password, setPassword] = useState(temporaryPassword);
  return (
    <form className="card stack" onSubmit={(e) => { e.preventDefault(); onSave(password); }} aria-label={`Reset password for ${user.id}`}>
      <h2>Reset password · {user.displayName}</h2>
      <p className="muted small">
        Give this temporary password to {user.displayName} directly, not by email. Their open sessions end now, any lockout is cleared,
        and they must choose a new password when they next sign in.
      </p>
      <div className="field">
        <label htmlFor="reset-password">Temporary password</label>
        <span className="password-row">
          <input id="reset-password" value={password} onChange={(e) => setPassword(e.target.value)} className="mono" required />
          <button type="button" onClick={() => setPassword(temporaryPassword())} aria-label="Generate a new temporary password">New</button>
        </span>
      </div>
      <div className="row">
        <button className="primary" type="submit">Reset password</button>
        <button type="button" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}

function RoleGuide() {
  const rows: [UserRole, string][] = [
    ["Student", "Own case only: upload transcripts and syllabi, follow status and secured units."],
    ["Staff", "Open cases, add courses, run matching, triage email, confirm an approved equivalency as it stands, post results."],
    ["Evaluator", "Review AI suggestions and evidence, record and finalise any decision."],
    ["Advisor", "Read-only student view of every case. No reviewer notes, no decisions."],
    ["Admin", "Everything staff and evaluators can do, plus rule settings and these accounts."],
  ];
  return (
    <section className="card stack">
      <h2>What each role can do</h2>
      <dl className="kv">
        {rows.map(([r, text]) => <Fragment key={r}><dt>{roleLabel[r]}</dt><dd>{text}</dd></Fragment>)}
      </dl>
    </section>
  );
}
