import type { Lang } from '../core/i18n/translations';

/** Decimal and thousands marks per UI language: 1.234,5 in Danish, 1,234.5 in English. */
const MARKS: Record<Lang, { decimal: string; group: string }> = {
  da: { decimal: ',', group: '.' },
  en: { decimal: '.', group: ',' },
};

interface Parts {
  negative: boolean;
  whole: string;
  /** Digits after the decimal mark; null when there is no decimal mark. */
  fraction: string | null;
}

/**
 * Reads a number typed in the UI language, with or without thousands separators.
 * The other decimal mark is understood too when it can't be a thousands separator, as phones
 * often give one or the other: "24,07" and "24.07" both work, "1.234" is 1234 in Danish.
 * Empty or unreadable text is null.
 */
export function parseDecimal(raw: string | null | undefined, lang: Lang): number | null {
  const parts = split(raw ?? '', lang, false);
  if (!parts || (parts.whole === '' && !parts.fraction)) return null;
  const value = Number(
    `${parts.negative ? '-' : ''}${parts.whole || '0'}.${parts.fraction || '0'}`,
  );
  return Number.isFinite(value) ? value : null;
}

/** Writes a number for a text field in the UI language, with thousands separators. */
export function formatDecimal(value: number | null | undefined, lang: Lang): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '';
  let text = Math.abs(value).toString();
  if (text.includes('e'))
    text = Math.abs(value)
      .toFixed(10)
      .replace(/\.?0+$/, '');
  const [whole, fraction] = text.split('.');
  return join({ negative: value < 0, whole, fraction: fraction ?? null }, lang);
}

/**
 * Regroups text being typed into a number field, or null to leave it as it is. While typing,
 * the language's thousands separator only ever groups digits, so deleting a digit from
 * "12.345" gives "1.245" rather than a decimal.
 */
export function regroupTyped(raw: string, lang: Lang, typing: boolean): string | null {
  const parts = split(raw, lang, typing);
  if (!parts || (parts.whole === '' && parts.fraction === null)) return null;
  // A leading zero goes once a digit follows it, and a bare decimal mark gets one in front.
  parts.whole = parts.whole.replace(/^0+(?=\d)/, '') || '0';
  return join(parts, lang);
}

/** Whether a character counts towards the caret position: a digit, sign or decimal mark. */
export function isSignificant(char: string, lang: Lang): boolean {
  return /[\d-]/.test(char) || char === MARKS[lang].decimal;
}

function join({ negative, whole, fraction }: Parts, lang: Lang): string {
  const { decimal, group } = MARKS[lang];
  const grouped = whole.replace(/\B(?=(\d{3})+$)/g, group);
  return `${negative ? '-' : ''}${grouped}${fraction === null ? '' : decimal + fraction}`;
}

function split(raw: string, lang: Lang, typing: boolean): Parts | null {
  const match = /^\s*(-?)([\d.,\s  ]*)$/.exec(raw);
  if (!match) return null;
  const body = match[2].replace(/[\s  ]/g, '');
  const at = typing ? typedDecimalAt(body, lang) : decimalAt(body, lang);
  if (at === null) return null;
  const whole = (at === -1 ? body : body.slice(0, at)).replace(/[.,]/g, '');
  let fraction = at === -1 ? null : body.slice(at + 1);
  if (typing && fraction !== null) fraction = fraction.split(MARKS[lang].group).join('');
  if (fraction !== null && /[.,]/.test(fraction)) return null;
  return { negative: match[1] === '-', whole, fraction };
}

/** Index of the decimal mark in text being typed: only the language's own mark. */
function typedDecimalAt(body: string, lang: Lang): number | null {
  const { decimal } = MARKS[lang];
  const at = body.indexOf(decimal);
  return at !== body.lastIndexOf(decimal) ? null : at;
}

/** Index of the decimal mark in digits and marks, -1 for none, null when unreadable. */
function decimalAt(body: string, lang: Lang): number | null {
  const lastComma = body.lastIndexOf(',');
  const lastDot = body.lastIndexOf('.');
  const last = Math.max(lastComma, lastDot);
  if (last === -1) return -1;
  const mark = body[last];
  // With both marks, the last one is the decimal mark and the other groups thousands.
  if (lastComma !== -1 && lastDot !== -1) {
    return body.indexOf(mark) === last ? last : null;
  }
  if (mark === MARKS[lang].decimal) return body.indexOf(mark) === last ? last : null;
  // Only the thousands mark: groups of three, unless the text after its one use isn't
  // three digits, which makes it a decimal mark ("24.07" in Danish).
  const groups = body.split(mark);
  if (groups.slice(1).every((g) => g.length === 3)) return -1;
  return groups.length === 2 ? last : null;
}
