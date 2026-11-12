import { parseSseEvent } from '../../data-access/assistant-stream.client';
import { formatValue, safeLink } from './format-value';

describe('formatValue', () => {
  it('formats canonical dates in the given locale without shifting the day', () => {
    const text = formatValue('2026-12-25', 'DATE', 'en-GB');
    expect(text).toContain('25 Dec 2026');
    expect(text).toContain('Fri');
  });

  it('formats numbers and sizes for the locale', () => {
    expect(formatValue('1234.5', 'NUMBER', 'en-US')).toBe('1,234.5');
    expect(formatValue('2457600', 'BYTES', 'en-US')).toBe('2.3 MB');
  });

  it('shows booleans as Yes/No and empty values as a dash', () => {
    expect(formatValue('true', 'BOOLEAN', 'en-US')).toBe('Yes');
    expect(formatValue('', 'TEXT', 'en-US')).toBe('—');
  });

  it('leaves unparseable values as they are', () => {
    expect(formatValue('soon', 'DATE', 'en-US')).toBe('soon');
    expect(formatValue('n/a', 'NUMBER', 'en-US')).toBe('n/a');
  });

  it('only renders http(s) links', () => {
    expect(safeLink('https://contoso.sharepoint.com/x')).toBe('https://contoso.sharepoint.com/x');
    expect(safeLink('javascript:alert(1)')).toBeNull();
  });
});

describe('parseSseEvent', () => {
  it('reads the event name and JSON payload', () => {
    expect(parseSseEvent('event: delta\ndata: {"text":"Hi"}')).toEqual({ type: 'delta', text: 'Hi' });
  });

  it('ignores malformed events', () => {
    expect(parseSseEvent('event: delta\ndata: {oops')).toBeNull();
    expect(parseSseEvent(': keep-alive')).toBeNull();
  });
});
