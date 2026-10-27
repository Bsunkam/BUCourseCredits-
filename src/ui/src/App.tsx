import { useCallback, useEffect, useState } from "react";
import { Navigate, NavLink, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { api, ApiError, SIGNED_OUT_EVENT } from "./api";
import { isAdmin, isReviewer, isStaff, Loading, UserContext } from "./components";
import { roleLabel } from "./labels";
import type { CurrentUser } from "./types";
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
import LoginPage from "./pages/LoginPage";
import AccountPage from "./pages/AccountPage";
import UsersPage from "./pages/UsersPage";

const home = (user: CurrentUser) => user.role === "Student" ? "/my" : user.role === "Advisor" ? "/cases" : "/dashboard";

export default function App() {
  // undefined: still asking the API who is signed in; null: nobody.
  const [user, setUser] = useState<CurrentUser | null | undefined>(undefined);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const navigate = useNavigate();
  const location = useLocation();

  useEffect(() => {
    api.get<CurrentUser>("/api/me")
      .then(setUser)
      .catch((e) => (e instanceof ApiError && e.status === 401 ? setUser(null) : setError(e.message)));
  }, []);

  // Any 401 means the session is gone (expired, signed out elsewhere, account disabled or changed).
  useEffect(() => {
    const ended = () => {
      setUser(null);
      setNotice("Your session has ended. Sign in again to continue.");
    };
    window.addEventListener(SIGNED_OUT_EVENT, ended);
    return () => window.removeEventListener(SIGNED_OUT_EVENT, ended);
  }, []);

  // Return to the page that asked for sign-in (a shared case link, say); the role's routes still decide what opens.
  const signedIn = useCallback((u: CurrentUser) => {
    setNotice(null);
    setUser(u);
    const wanted = location.pathname;
    const target = u.mustChangePassword ? "/account"
      : wanted === "/" || wanted === "/login" || wanted === "/overview" ? home(u) : wanted + location.search;
    navigate(target, { replace: true });
  }, [navigate, location.pathname, location.search]);

  async function signOut() {
    try { await api.post("/api/auth/logout"); } catch { /* the session is dropped locally either way */ }
    setUser(null);
    setNotice("You have signed out.");
    navigate("/login", { replace: true });
  }

  if (error) return <main><div className="alert error">Cannot reach the API: {error}</div></main>;
  if (user === undefined) return <Loading what="Starting" />;

  return (
    <UserContext.Provider value={user}>
      <div className="shell">
      <aside className="sidebar">
        <span className="brand"><span className="brand-mark">TC</span>Transfer Credit Review</span>
        <nav className="nav" aria-label="Main">
          {(!user || !user.mustChangePassword) && <NavLink to="/overview">Overview</NavLink>}
          {user && !user.mustChangePassword && (
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
                {isAdmin(user) && <NavLink to="/admin/users">Users & roles</NavLink>}
              </>
            )
          )}
        </nav>
        <span className="spacer" />
        {user ? (
          <div className="user-box">
            <span className="user-name">{user.displayName}</span>
            <span className="user-role">{roleLabel[user.role]}</span>
            <div className="row">
              {!user.mustChangePassword && <NavLink to="/account" className="user-link">Account</NavLink>}
              <button onClick={() => void signOut()}>Sign out</button>
            </div>
          </div>
        ) : (
          <NavLink to="/login" className="button primary sign-in-link">Sign in</NavLink>
        )}
      </aside>
      <div className="content">
      <div className="demo-banner">
        Prototype with synthetic data only. AI suggestions are advisory; the academic department makes every final
        decision. Posting to MyBU Student is simulated.
      </div>
      <main>
        {!user ? (
          <Routes>
            <Route path="/login" element={<LoginPage onSignedIn={signedIn} notice={notice} />} />
            <Route path="/overview" element={<OverviewPage />} />
            {/* Any other page needs a session: show sign-in, then land on the role's home page. */}
            <Route path="*" element={<LoginPage onSignedIn={signedIn} notice={notice} />} />
          </Routes>
        ) : user.mustChangePassword ? (
          <AccountPage required onChanged={(u) => { setUser(u); navigate(home(u), { replace: true }); }} />
        ) : (
          <Routes>
            <Route path="/" element={<Navigate to={home(user)} replace />} />
            <Route path="/login" element={<Navigate to={home(user)} replace />} />
            <Route path="/account" element={<AccountPage onChanged={setUser} />} />
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
            <Route path="/admin/users" element={isAdmin(user) ? <UsersPage /> : <Navigate to="/" replace />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        )}
      </main>
      </div>
      </div>
    </UserContext.Provider>
  );
}
