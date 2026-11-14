import { lazy, Suspense } from 'react';
import { BrowserRouter, Link, Navigate, Outlet, Route, Routes } from 'react-router';
import { SessionProvider, useSession } from '../platform/session/SessionProvider';
import { AppShell } from './AppShell';
const LoginPage = lazy(() => import('../modules/auth/AuthPage').then(module => ({ default: module.AuthPage })));
const Prototype = lazy(() => import('../modules/invoices/InvoicePrototypePage').then(module => ({ default: module.InvoicePrototypePage })));
const InvoicesPage = lazy(() => import('../modules/invoices/InvoicesPage').then(module => ({ default: module.InvoicesPage })));
function Pending() { return <main className="status-page" role="status">Wczytywanie…</main>; }
function SessionLayout() { return <SessionProvider><SessionGate /></SessionProvider>; }
function SessionGate() {
  const { state, store } = useSession();
  if (state.status === 'loading') return <Pending />;
  if (state.status === 'error') return <main className="status-page"><h1>Nie udało się wczytać sesji</h1><p role="alert">{state.error}</p>
    <button className="primary-button" disabled={state.busy} onClick={() => { void (state.recovery === 'logout' ? store.logout() : store.restore()); }}>Spróbuj ponownie</button></main>;
  return <Outlet />;
}
function Protected() { const { state } = useSession(); return state.status === 'authenticated' ? <AppShell /> : <Navigate to="/login" replace />; }
function Anonymous({ register = false }: { register?: boolean }) {
  const { state } = useSession(); return state.status === 'authenticated' ? <Navigate to="/app/invoices" replace /> : <LoginPage key={register ? 'register' : 'login'} register={register} />;
}
function Home() { const { state } = useSession(); return <Navigate to={state.status === 'authenticated' ? '/app/invoices' : '/login'} replace />; }
export function App() {
  return <BrowserRouter><Suspense fallback={<Pending />}><Routes>
    <Route path="/prototype/invoices" element={<Prototype />} />
    <Route element={<SessionLayout />}>
      <Route index element={<Home />} />
      <Route path="login" element={<Anonymous />} />
      <Route path="register" element={<Anonymous register />} />
      <Route path="app" element={<Protected />}>
        <Route index element={<Navigate to="invoices/sales" replace />} />
        <Route path="invoices" element={<Navigate to="sales" replace />} />
        <Route path="invoices/sales" element={<InvoicesPage key="sales" direction="Sales" />} />
        <Route path="invoices/purchase" element={<InvoicesPage key="purchase" direction="Purchase" />} />
      </Route>
      <Route path="*" element={<main><h1>Nie znaleziono strony</h1><Link to="/">Strona główna</Link></main>} />
    </Route>
  </Routes></Suspense></BrowserRouter>;
}
