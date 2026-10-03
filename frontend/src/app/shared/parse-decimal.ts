/**
 * Reads a number typed with either decimal mark, as phones in Danish give a comma:
 * "24,07", "24.07", "1.234,5" and "1 234,5" all work. Empty or unreadable text is null.
 */
export function parseDecimal(raw: string | null | undefined): number | null {
  const text = (raw ?? '').replace(/[\s ]/g, '');
  if (text === '') return null;
  const lastComma = text.lastIndexOf(',');
  const lastDot = text.lastIndexOf('.');
  // The mark that comes last is the decimal mark; the other one groups thousands.
  const normalized =
    lastComma > lastDot ? text.replace(/\./g, '').replace(',', '.') : text.replace(/,/g, '');
  if (!/^-?\d*\.?\d+$|^-?\d+\.$/.test(normalized)) return null;
  const value = Number(normalized);
  return Number.isFinite(value) ? value : null;
}
