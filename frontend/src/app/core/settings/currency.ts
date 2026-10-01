import { Injectable, afterNextRender, effect, signal } from '@angular/core';
import { readStorage, writeStorage } from '../storage';

export interface CurrencyOption {
  code: string;
  /** BCP 47 locale used for number formatting. */
  locale: string;
}

/** Add a currency here (and its name in i18n/translations.ts) to make it selectable. */
export const CURRENCIES: readonly CurrencyOption[] = [
  { code: 'DKK', locale: 'da-DK' },
  { code: 'EUR', locale: 'de-DE' },
  { code: 'SEK', locale: 'sv-SE' },
  { code: 'NOK', locale: 'nb-NO' },
  { code: 'USD', locale: 'en-US' },
  { code: 'GBP', locale: 'en-GB' },
];

export const DEFAULT_CURRENCY = 'DKK';

const STORAGE_KEY = 'firecalc.currency';

@Injectable({ providedIn: 'root' })
export class CurrencySettings {
  readonly current = signal<CurrencyOption>(CURRENCIES.find((c) => c.code === DEFAULT_CURRENCY)!);

  private readonly restored = signal(false);

  constructor() {
    // The server renders the default currency; the saved choice is applied after hydration.
    afterNextRender(() => {
      this.select(readStorage(STORAGE_KEY) ?? DEFAULT_CURRENCY);
      this.restored.set(true);
    });
    effect(() => {
      const code = this.current().code;
      if (this.restored()) writeStorage(STORAGE_KEY, code);
    });
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
