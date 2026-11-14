import { useMemo, useState } from 'react';
import { InvoiceTable } from './InvoiceTable';
import { createDemoInvoices } from './demoInvoices';
import { formatCount } from '../../shared/formatting/polish';
import type { InvoiceDirection } from './Invoice';

export function InvoicePrototypePage({ embedded = false }: { embedded?: boolean }) {
  const [direction, setDirection] = useState<InvoiceDirection>('sales');
  const [count, setCount] = useState(10_000);
  const [revision, setRevision] = useState(0);
  const invoices = useMemo(() => createDemoInvoices(count, direction), [count, direction]);
  return <>
    {!embedded && <header className="app-header">
      <a className="brand" href="/prototype/invoices" aria-label="xKsef — strona główna"><span className="brand-mark">K</span><span>xKsef</span></a>
      <span className="header-caption">Finanse i księgowość</span>
      <span className="demo-badge"><span aria-hidden="true">●</span> Dane przykładowe</span>
    </header>}
    <main>
      <div className="breadcrumb">Dokumenty <span aria-hidden="true">/</span> Faktury</div>
      <div className="page-heading"><div><h1>Faktury</h1><p>Sprzedaż i zakup w jednym miejscu.</p></div>
        <label className="dataset-label">Liczba faktur<select value={count} onChange={event => setCount(Number(event.target.value))}>
          {[1_000, 10_000, 100_000].map(size => <option key={size} value={size}>{formatCount(size)}</option>)}
        </select></label>
      </div>
      <div className="invoice-navigation" role="group" aria-label="Rodzaj faktur">
        <button aria-pressed={direction === 'sales'} onClick={() => setDirection('sales')}>Sprzedaż</button>
        <button aria-pressed={direction === 'purchase'} onClick={() => setDirection('purchase')}>Zakup</button>
      </div>
      <InvoiceTable key={`${direction}-${count}-${revision}`} invoices={invoices} direction={direction} onReset={() => setRevision(value => value + 1)} />
      <p className="prototype-note">Prototyp M14.1 · Wyłącznie syntetyczne dane, bez połączenia z API faktur i KSeF.</p>
    </main>
  </>;
}
