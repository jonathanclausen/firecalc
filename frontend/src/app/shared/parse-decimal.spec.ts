import { formatDecimal, parseDecimal, regroupTyped } from './parse-decimal';

describe('parseDecimal', () => {
  it('reads a decimal comma or dot', () => {
    expect(parseDecimal('24,07', 'da')).toBe(24.07);
    expect(parseDecimal('24.07', 'da')).toBe(24.07);
    expect(parseDecimal('24,07', 'en')).toBe(24.07);
    expect(parseDecimal('24.07', 'en')).toBe(24.07);
    expect(parseDecimal('537', 'da')).toBe(537);
    expect(parseDecimal('-1,5', 'da')).toBe(-1.5);
    expect(parseDecimal('12,', 'da')).toBe(12);
  });

  it('skips thousands separators', () => {
    expect(parseDecimal('1.234,5', 'da')).toBe(1234.5);
    expect(parseDecimal('1,234.5', 'da')).toBe(1234.5);
    expect(parseDecimal('1 234,5', 'da')).toBe(1234.5);
    expect(parseDecimal('1,234,567.5', 'en')).toBe(1234567.5);
  });

  it("reads a lone thousands separator by the language's convention", () => {
    expect(parseDecimal('1.234', 'da')).toBe(1234);
    expect(parseDecimal('1.234.567', 'da')).toBe(1234567);
    expect(parseDecimal('1,234', 'en')).toBe(1234);
  });

  it('gives null for empty or unreadable text', () => {
    expect(parseDecimal('', 'da')).toBeNull();
    expect(parseDecimal('  ', 'da')).toBeNull();
    expect(parseDecimal(undefined, 'da')).toBeNull();
    expect(parseDecimal('abc', 'da')).toBeNull();
    expect(parseDecimal('1,2,3', 'da')).toBeNull();
    expect(parseDecimal('1,2,3', 'en')).toBeNull();
    expect(parseDecimal('-', 'da')).toBeNull();
  });
});

describe('formatDecimal', () => {
  it('groups thousands in the language', () => {
    expect(formatDecimal(1234567.5, 'da')).toBe('1.234.567,5');
    expect(formatDecimal(1234567.5, 'en')).toBe('1,234,567.5');
    expect(formatDecimal(-1234, 'da')).toBe('-1.234');
    expect(formatDecimal(999, 'da')).toBe('999');
    expect(formatDecimal(0.0000001, 'en')).toBe('0.0000001');
    expect(formatDecimal(null, 'da')).toBe('');
  });

  it('reads back what it writes', () => {
    for (const value of [1.234, 1234, 1234567.891, -0.5, 1000000]) {
      expect(parseDecimal(formatDecimal(value, 'da'), 'da')).toBe(value);
      expect(parseDecimal(formatDecimal(value, 'en'), 'en')).toBe(value);
    }
  });
});

describe('regroupTyped', () => {
  it('regroups digits as they are typed or deleted', () => {
    expect(regroupTyped('1234', 'da', true)).toBe('1.234');
    expect(regroupTyped('1.2345', 'da', true)).toBe('12.345');
    expect(regroupTyped('12.45', 'da', true)).toBe('1.245');
    expect(regroupTyped('1.234,5', 'da', true)).toBe('1.234,5');
    expect(regroupTyped('1234,', 'da', true)).toBe('1.234,');
    expect(regroupTyped('1,2345', 'en', true)).toBe('12,345');
    expect(regroupTyped('1234.5', 'en', true)).toBe('1,234.5');
  });

  it('drops a typed thousands separator and leading zeros', () => {
    expect(regroupTyped('1.000.', 'da', true)).toBe('1.000');
    expect(regroupTyped('05', 'da', true)).toBe('5');
    expect(regroupTyped(',5', 'da', true)).toBe('0,5');
  });

  it('reads pasted text either way', () => {
    expect(regroupTyped('24.07', 'da', false)).toBe('24,07');
    expect(regroupTyped('1234567.5', 'en', false)).toBe('1,234,567.5');
  });

  it('leaves text that is not a number yet', () => {
    expect(regroupTyped('', 'da', true)).toBeNull();
    expect(regroupTyped('-', 'da', true)).toBeNull();
    expect(regroupTyped('1,2,3', 'da', true)).toBeNull();
    expect(regroupTyped('abc', 'da', true)).toBeNull();
  });
});
