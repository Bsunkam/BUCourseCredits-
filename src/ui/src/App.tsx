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
import OverviewPage from "./pages/OverviewPage";

const home = (user: DemoUser) => user.role === "Student" ? "/my" : user.role === "Advisor" ? "/cases" : "/dashboard";

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
        <nav className="nav" aria-label="Main">
          <NavLink to="/overview" className={({ isActive }) => (isActive || !user ? "active" : "")}>Overview</NavLink>
          {user && (
            user.role === "Student" ? (
              <NavLink to="/my">My transfer credit</NavLink>
            ) : user.role === "Advisor" ? (
              // Advisors follow progress but never decide equivalency: read-only, student-safe summaries only.
              <NavLink to="/cases">Advisee cases (read-only)</NavLink>
            ) : (
              <>
                <NavLink to="/dashboard">Dashboard</NavLink>
                <NavLink to="/cases">Cases</NavLink>
                {isStaff(user) && <NavLink to="/intake">Email triage</NavLink>}
                <NavLink to="/catalogue">Catalogue & rules</NavLink>
                <NavLink to="/metrics">Metrics & AI evidence</NavLink>
              </>
            )
          )}
        </nav>
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
          <OverviewPage />
        ) : (
          <Routes>
            <Route path="/" element={<Navigate to={home(user)} replace />} />
            <Route path="/my" element={<StudentPage />} />
            <Route path="/overview" element={<OverviewPage signedIn />} />
            <Route path="/dashboard" element={isReviewer(user) ? <DashboardPage /> : <Navigate to="/" replace />} />
            <Route path="/cases" element={<CasesPage />} />
            <Route path="/cases/:id" element={isReviewer(user) ? <CaseDetailPage /> : <Navigate to="status" replace />} />
            <Route path="/cases/:id/status" element={<StatusPage />} />
            <Route path="/courses/:id" element={<WorkbenchPage />} />
            <Route path="/intake" element={<IntakePage />} />
            <Route path="/catalogue" element={isReviewer(user) ? <CataloguePage /> : <Navigate to="/" replace />} />
            <Route path="/metrics" element={isReviewer(user) ? <MetricsPage /> : <Navigate to="/" replace />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        )}
      </main>
      </div>
      </div>
    </UserContext.Provider>
  );
}

