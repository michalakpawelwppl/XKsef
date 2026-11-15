import type { ICellRendererParams } from 'ag-grid-community';

interface CopyCellRendererProps<T> extends ICellRendererParams<T> {
  format?: (value: unknown) => string;
  isMoney?: boolean;
}

export function CopyCellRenderer<T>({ value, format, isMoney }: CopyCellRendererProps<T>) {
  const text = value === null || value === undefined ? '' : format ? format(value) : String(value);
  const copyText = isMoney ? text.replace(/[\s\u00A0\u202F]/g, '') : text;

  const copy = async () => {
    await navigator.clipboard.writeText(copyText);
  };

  return (
    <div className="copy-cell">
      <span className="copy-cell-text">{text}</span>
      <button type="button" className="copy-cell-button" title="Kopiuj do schowka" aria-label="Kopiuj wartość komórki" onMouseDown={event => event.stopPropagation()} onClick={event => { event.stopPropagation(); void copy(); }}>
        ⧉
      </button>
    </div>
  );
}