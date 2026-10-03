import { parseDecimal } from './parse-decimal';

describe('parseDecimal', () => {
  it('reads a decimal comma or dot', () => {
    expect(parseDecimal('24,07')).toBe(24.07);
    expect(parseDecimal('24.07')).toBe(24.07);
    expect(parseDecimal('537')).toBe(537);
    expect(parseDecimal('-1,5')).toBe(-1.5);
  });

  it('skips thousands separators', () => {
    expect(parseDecimal('1.234,5')).toBe(1234.5);
    expect(parseDecimal('1,234.5')).toBe(1234.5);
    expect(parseDecimal('1 234,5')).toBe(1234.5);
  });

  it('gives null for empty or unreadable text', () => {
    expect(parseDecimal('')).toBeNull();
    expect(parseDecimal('  ')).toBeNull();
    expect(parseDecimal(undefined)).toBeNull();
    expect(parseDecimal('abc')).toBeNull();
    expect(parseDecimal('1,2,3')).toBeNull();
  });
});
