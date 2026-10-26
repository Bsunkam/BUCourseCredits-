import { useEffect, useState } from "react";
import { Navigate, NavLink, Route, Routes, useNavigate } from "react-router-dom";
import { api, getDemoUser, setDemoUser } from "./api";
import { isReviewer, isStaff, Loading, UserContext } from "./components";
import type { DemoUser } from "./types";
import CasesPage from "./pages/CasesPage";
import CaseDetailPage from "./pages/CaseDetailPage";
import WorkbenchPage from "./pages/WorkbenchPage";
import IntakePage from "./pages/IntakePage";
import CataloguePage from "./pages/CataloguePage";
import StudentPage from "./pages/StudentPage";
import StatusPage from "./pages/StatusPage";
import MetricsPage from "./pages/MetricsPage";
import DashboardPage from "./pages/DashboardPage";

export default function App() {
  const [users, setUsers] = useState<DemoUser[] | null>(null);
  const [userId, setUserId] = useState<string | null>(getDemoUser());
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();

  useEffect(() => {
    api.get<DemoUser[]>("/api/demo/users").then(setUsers).catch((e) => setError(e.message));
  }, []);

  const user = users?.find((u) => u.id === userId) ?? null;

  function switchUser(id: string) {
    setDemoUser(id || null);
    setUserId(id || null);
    navigate("/");
  }

  if (error) return <main><div className="alert error">Cannot reach the API: {error}</div></main>;
  if (!users) return <Loading what="Starting" />;

  return (
    <UserContext.Provider value={user}>
      <div className="shell">
      <aside className="sidebar">
        <span className="brand"><span className="brand-mark">TC</span>Transfer Credit Review</span>
        {user && (
          <nav className="nav" aria-label="Main">
            {user.role === "Student" ? (
              <NavLink to="/my">My transfer credit</NavLink>
            ) : (
              <>
                <NavLink to="/dashboard">Dashboard</NavLink>
                <NavLink to="/cases">Cases</NavLink>
                {isStaff(user) && <NavLink to="/intake">Email triage</NavLink>}
                <NavLink to="/catalogue">Catalogue & rules</NavLink>
                <NavLink to="/metrics">Metrics & AI evidence</NavLink>
              </>
            )}
          </nav>
        )}
        <span className="spacer" />
        <label className="field">
          Demo user
          <select value={userId ?? ""} onChange={(e) => switchUser(e.target.value)} aria-label="Demo user">
            <option value="">Choose…</option>
            {users.map((u) => <option key={u.id} value={u.id}>{u.displayName}</option>)}
          </select>
        </label>
      </aside>
      <div className="content">
      <div className="demo-banner">
        Prototype with synthetic data only. AI suggestions are advisory; the academic department makes every final
        decision. Posting to MyBU Student is simulated.
      </div>
      <main>
        {!user ? (
          <SignIn users={users} onPick={switchUser} />
        ) : (
          <Routes>
            <Route path="/" element={<Navigate to={user.role === "Student" ? "/my" : "/dashboard"} replace />} />
            <Route path="/my" element={<StudentPage />} />
            <Route path="/dashboard" element={<DashboardPage />} />
            <Route path="/cases" element={<CasesPage />} />
            <Route path="/cases/:id" element={isReviewer(user) ? <CaseDetailPage /> : <Navigate to="status" replace />} />
            <Route path="/cases/:id/status" element={<StatusPage />} />
            <Route path="/courses/:id" element={<WorkbenchPage />} />
            <Route path="/intake" element={<IntakePage />} />
            <Route path="/catalogue" element={<CataloguePage />} />
            <Route path="/metrics" element={<MetricsPage />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        )}
      </main>
      </div>
      </div>
    </UserContext.Provider>
  );
}

function SignIn({ users, onPick }: { users: DemoUser[]; onPick: (id: string) => void }) {
  return (
    <div className="card stack" style={{ maxWidth: 560, margin: "40px auto" }}>
      <h1>Choose a demo user</h1>
      <p className="muted">
        Each role sees a different view. Demo sign-in only; a real deployment uses Microsoft Entra ID.
      </p>
      <div className="stack">
        {users.map((u) => (
          <button key={u.id} onClick={() => onPick(u.id)} style={{ textAlign: "left" }}>
            <strong>{u.displayName}</strong> <span className="muted small">· {u.role}</span>
          </button>
        ))}
      </div>
    </div>
  );
}
