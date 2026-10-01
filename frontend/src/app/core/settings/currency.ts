import { Injectable, effect, signal } from '@angular/core';
import { readStorage, writeStorage } from '../storage';

export interface CurrencyOption {
  code: string;
  /** BCP 47 locale used for number formatting. */
  locale: string;
  label: string;
}

/** Add a currency here to make it selectable in the header. */
export const CURRENCIES: readonly CurrencyOption[] = [
  { code: 'DKK', locale: 'da-DK', label: 'Danish krone' },
  { code: 'EUR', locale: 'de-DE', label: 'Euro' },
  { code: 'SEK', locale: 'sv-SE', label: 'Swedish krona' },
  { code: 'NOK', locale: 'nb-NO', label: 'Norwegian krone' },
  { code: 'USD', locale: 'en-US', label: 'US dollar' },
  { code: 'GBP', locale: 'en-GB', label: 'British pound' },
];

export const DEFAULT_CURRENCY = 'DKK';

const STORAGE_KEY = 'firecalc.currency';

@Injectable({ providedIn: 'root' })
export class CurrencySettings {
  readonly current = signal<CurrencyOption>(
    CURRENCIES.find((c) => c.code === readStorage(STORAGE_KEY)) ??
      CURRENCIES.find((c) => c.code === DEFAULT_CURRENCY)!,
  );

  constructor() {
    effect(() => writeStorage(STORAGE_KEY, this.current().code));
  }

  select(code: string) {
    const match = CURRENCIES.find((c) => c.code === code);
    if (match) this.current.set(match);
  }

  format(value: number, options: { compact?: boolean } = {}) {
    const { code, locale } = this.current();
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency: code,
      maximumFractionDigits: options.compact ? 1 : 0,
      notation: options.compact ? 'compact' : 'standard',
    }).format(value);
  }
}
