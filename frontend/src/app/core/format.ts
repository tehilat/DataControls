import { DataType, OptionDto } from './api/models';

const numberFormat = new Intl.NumberFormat('he-IL', { maximumFractionDigits: 4 });

const pad = (n: number) => String(n).padStart(2, '0');

/** "2026-03-01" → "01/03/2026" (no time-zone conversion: the value is a calendar date). */
export function formatDate(value: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  return m ? `${m[3]}/${m[2]}/${m[1]}` : value;
}

/** "2026-03-01T14:05:00.0000000" → "01/03/2026 14:05" (server local time, shown as-is). */
export function formatDateTime(value: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})/.exec(value);
  return m ? `${m[3]}/${m[2]}/${m[1]} ${m[4]}:${m[5]}` : formatDate(value);
}

export function formatNumber(value: number, unit?: string | null): string {
  const text = numberFormat.format(value);
  return unit ? `${text} ${unit}` : text;
}

/** Formats a canonical value (as returned in run results and as stored in definitions) for display. */
export function formatValue(value: unknown, dataType: DataType, unit?: string | null, options?: readonly OptionDto[]): string {
  if (value === null || value === undefined || value === '') return '—';
  switch (dataType) {
    case 'number':
      return typeof value === 'number' ? formatNumber(value, unit) : String(value);
    case 'date':
      return formatDate(String(value));
    case 'datetime':
      return formatDateTime(String(value));
    case 'boolean':
      return value === true || value === 'true' ? 'כן' : 'לא';
    case 'enum':
      return options?.find((o) => o.value === value)?.displayName ?? String(value);
    default:
      return String(value);
  }
}

/** Local date → "yyyy-MM-dd" (no UTC shift). */
export function toIsoDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** "yyyy-MM-dd" → local Date at midnight. */
export function fromIsoDate(value: unknown): Date | null {
  const m = typeof value === 'string' ? /^(\d{4})-(\d{2})-(\d{2})/.exec(value) : null;
  return m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : null;
}

/** Duration in a short Hebrew form. */
export function formatDuration(ms: number | null | undefined): string {
  if (ms === null || ms === undefined) return '—';
  if (ms < 1000) return `${ms} מ"ש`;
  return `${(ms / 1000).toLocaleString('he-IL', { maximumFractionDigits: 1 })} שניות`;
}
