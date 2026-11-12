import type { FieldFormat } from '../../../../graphql/generated';

/**
 * Formats a block value for the employee's locale. The API sends values in canonical, culture-invariant form
 * (dates as yyyy-MM-dd, date-times as ISO 8601, numbers with a '.' decimal point), so formatting is done here.
 */
export function formatValue(value: string, format: FieldFormat, locale: string): string {
  if (!value) return '—';
  try {
    switch (format) {
      case 'DATE': {
        const [y, m, d] = value.split('-').map(Number);
        if (!y || !m || !d) return value;
        return new Intl.DateTimeFormat(locale, { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' }).format(
          new Date(Date.UTC(y, m - 1, d)),
        );
      }
      case 'DATE_TIME': {
        const date = new Date(value);
        return Number.isNaN(date.getTime())
          ? value
          : new Intl.DateTimeFormat(locale, { weekday: 'short', day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' }).format(date);
      }
      case 'NUMBER': {
        const n = Number(value);
        return Number.isFinite(n) ? new Intl.NumberFormat(locale, { maximumFractionDigits: 2 }).format(n) : value;
      }
      case 'BOOLEAN':
        return value === 'true' ? 'Yes' : value === 'false' ? 'No' : value;
      case 'BYTES':
        return formatBytes(Number(value), locale) ?? value;
      default:
        return value;
    }
  } catch {
    return value;
  }
}

/** Only http(s) links are rendered as links. */
export function safeLink(value: string): string | null {
  return /^https?:\/\//i.test(value) ? value : null;
}

/** Numbers, dates and sizes are right-aligned in tables so they can be compared down a column. */
export function isNumeric(format: FieldFormat): boolean {
  return format === 'NUMBER' || format === 'BYTES';
}

function formatBytes(bytes: number, locale: string): string | null {
  if (!Number.isFinite(bytes)) return null;
  const units = ['bytes', 'KB', 'MB', 'GB', 'TB'];
  let size = bytes;
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit++;
  }
  return `${new Intl.NumberFormat(locale, { maximumFractionDigits: unit === 0 ? 0 : 1 }).format(size)} ${units[unit]}`;
}
