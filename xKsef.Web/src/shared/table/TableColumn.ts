export type TableCellValue = string | number | Date | null;

// Business modules provide values and presentation, without depending on AG Grid types.
export interface TableColumn<T> {
  readonly id: string;
  readonly header: string;
  readonly kind: 'text' | 'number' | 'date';
  readonly value: (row: T) => TableCellValue;
  readonly format?: (value: unknown) => string;
  readonly width?: number;
  readonly minWidth?: number;
}
