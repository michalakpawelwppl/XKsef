import { useState } from 'react';
import { NavLink, Outlet } from 'react-router';
import { useSession } from '../platform/session/SessionProvider';
const futureModules = ['Kontrahenci', 'Banki i rozrachunki', 'Księgowość', 'Podatki', 'Środki trwałe', 'Kadry i płace', 'Administracja'];
export function AppShell() {
  const [handelExpanded, setHandelExpanded] = useState(false);
  const { state, store } = useSession();
  const tenant = state.tenants.find(item => item.id === state.activeTenantId);
  return <div className="app-shell">
    <header className="app-header"><NavLink className="brand" to="/app/invoices"><span className="brand-mark">K</span>xKsef</NavLink>
      <span className="header-caption">Finanse i księgowość</span><span className="session-user">{state.user?.login}</span>
      <button className="secondary-button" onClick={() => { void store.logout(); }} disabled={state.busy}>Wyloguj</button></header>
    <aside className="sidebar"><p className="nav-caption">Moduły</p><nav aria-label="Moduły ERP">
<button type="button" aria-expanded={handelExpanded} aria-controls="handel-submenu" onClick={() => setHandelExpanded(value => !value)}>
  Handel
</button>
{handelExpanded && (
  <div id="handel-submenu" className="nav-submenu">
    <NavLink to="/app/invoices/purchase" onClick={() => setHandelExpanded(false)}>Zakup</NavLink>
    <NavLink to="/app/invoices/sales" onClick={() => setHandelExpanded(false)}>Sprzedaż</NavLink>
  </div>
)}
      {futureModules.map(label => <button key={label} disabled title="Moduł w przygotowaniu">{label}<small>W przygotowaniu</small></button>)}
    </nav></aside>
    <div className="workspace">
      <section className="tenant-bar" aria-label="Kontekst firmy"><label>Aktywny tenant<select value={state.activeTenantId ?? ''} onChange={event => store.selectTenant(event.target.value)}>
        <option value="" disabled>{state.tenants.length ? 'Wybierz tenanta' : 'Brak dostępnych tenantów'}</option>
        {state.tenants.map(item => <option key={item.id} value={item.id}>{item.name} · {item.id.slice(0, 8)}</option>)}
      </select></label><span className="tenant-role">{tenant ? `Rola: ${tenant.role}` : 'Nie wybrano tenanta'}</span>
      <button className="secondary-button" onClick={() => { void store.refreshTenants(); }}>Odśwież dostęp</button></section>
      {state.notice && <p className="workspace-message notice" role="status">{state.notice}</p>}
      {state.error && <p className="workspace-message error-message" role="alert">{state.error}</p>}
      <div key={`${state.sessionGeneration}:${state.tenantGeneration}`}><Outlet /></div>
    </div>
  </div>;
}
