import { useEffect, useRef, useState } from 'react';
import { NavLink, Outlet } from 'react-router';
import { useSession } from '../platform/session/SessionProvider';
const futureModules = ['Banki i rozrachunki', 'Księgowość', 'Podatki', 'Środki trwałe', 'Kadry i płace', 'Administracja'];
export function AppShell() {
  const [handelExpanded, setHandelExpanded] = useState(false);
  const [kartotekiExpanded, setKartotekiExpanded] = useState(false);
const [sidebarCollapsed, setSidebarCollapsed] = useState(true);
  const { state, store } = useSession();
  const tenant = state.tenants.find(item => item.id === state.activeTenantId);
  return <div className={`app-shell${sidebarCollapsed ? ' sidebar-collapsed' : ''}`}>
    <header className="app-header"><NavLink className="brand" to="/app/invoices"><span className="brand-mark">K</span>xKsef</NavLink>
      <span className="header-caption">Finanse i księgowość</span><span className="session-user">{state.user?.login}</span>
      <button className="secondary-button" onClick={() => { void store.logout(); }} disabled={state.busy}>Wyloguj</button></header>
    <aside className="sidebar">
  <div className="sidebar-header">
    <button
      type="button"
      className="sidebar-toggle"
      onClick={() => setSidebarCollapsed(value => !value)}
      aria-label={sidebarCollapsed ? 'Rozwiń menu' : 'Zwiń menu'}
      aria-expanded={!sidebarCollapsed}
      title={sidebarCollapsed ? 'Rozwiń menu' : 'Zwiń menu'}
    >
      <span className="hamburger-icon">
        <span />
        <span />
        <span />
      </span>
    </button>
    {!sidebarCollapsed && <span className="nav-caption">Moduły</span>}
  </div>
  <nav aria-label="Moduły ERP">
<button type="button" aria-expanded={handelExpanded && !sidebarCollapsed} aria-controls="handel-submenu" title="Handel" onClick={() => { if (sidebarCollapsed) setSidebarCollapsed(false); setHandelExpanded(value => !value); }}>
  {sidebarCollapsed ? 'H' : 'Handel'}
</button>
{handelExpanded && !sidebarCollapsed && (
  <div id="handel-submenu" className="nav-submenu">
<NavLink to="/app/invoices/purchase" onClick={() => { setHandelExpanded(false); setSidebarCollapsed(true); }}>Zakup</NavLink>
<NavLink to="/app/invoices/sales" onClick={() => { setHandelExpanded(false); setSidebarCollapsed(true); }}>Sprzedaż</NavLink>
  </div>
)}
<button type="button" aria-expanded={kartotekiExpanded && !sidebarCollapsed} aria-controls="kartoteki-submenu" title="Kartoteki" onClick={() => { if (sidebarCollapsed) setSidebarCollapsed(false); setKartotekiExpanded(value => !value); }}>
  {sidebarCollapsed ? 'K' : 'Kartoteki'}
</button>
{kartotekiExpanded && !sidebarCollapsed && (
  <div id="kartoteki-submenu" className="nav-submenu">
    <NavLink to="/app/contractors" onClick={() => { setKartotekiExpanded(false); setSidebarCollapsed(true); }}>Kontrahenci</NavLink>
  </div>
)}
     {futureModules.map(label => <button key={label} disabled title={`${label} — moduł w przygotowaniu`}>{sidebarCollapsed ? label.charAt(0) : label}{!sidebarCollapsed && <small>W przygotowaniu</small>}</button>)}
    </nav></aside>
    <div className="workspace">
      <section className="tenant-bar" aria-label="Kontekst firmy"><label>Aktywny tenant<select value={state.activeTenantId ?? ''} onChange={event => store.selectTenant(event.target.value)}>
        <option value="" disabled>{state.tenants.length ? 'Wybierz tenanta' : 'Brak dostępnych tenantów'}</option>
        {state.tenants.map(item => <option key={item.id} value={item.id}>{item.name} · {item.id.slice(0, 8)}</option>)}
      </select></label><span className="tenant-role">{tenant ? `Rola: ${tenant.role}` : 'Nie wybrano tenanta'}</span>
      <button className="secondary-button" onClick={() => { void store.refreshTenants(); }}>Odśwież dostęp</button></section>
      {state.notice && <p className="workspace-message notice" role="status">{state.notice}</p>}
      {state.error && <p className="workspace-message error-message" role="alert">{state.error}</p>}
      {state.verificationError && <VerificationError error={state.verificationError} onRetry={() => { void store.verify(); }} onLogout={() => { void store.logout(); }} />}
      <div key={`${state.sessionGeneration}:${state.tenantGeneration}`}><Outlet /></div>
    </div>
  </div>;
}

function VerificationError({ error, onRetry, onLogout }: { error: string; onRetry: () => void; onLogout: () => void }) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current;
    dialog?.showModal();
    return () => { dialog?.close(); };
  }, []);
  // Native top-layer dialog remains reachable above existing form portals without changing their CSS or state.
  return <dialog ref={ref} aria-label="Weryfikacja sesji" onCancel={event => event.preventDefault()}>
    <p role="alert">Nie udało się zweryfikować sesji. {error}</p>
    <button className="secondary-button" onClick={onRetry}>Ponów weryfikację sesji</button>
    <button className="secondary-button" onClick={onLogout}>Wyloguj</button>
  </dialog>;
}
