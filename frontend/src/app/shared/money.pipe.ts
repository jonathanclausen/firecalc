import { Pipe, PipeTransform, inject } from '@angular/core';
import { CurrencySettings } from '../core/settings/currency';

/**
 * Formats a number in the selected currency, or in a fixed one when given.
 * Impure so it reacts to currency changes.
 */
@Pipe({ name: 'money', pure: false })
export class MoneyPipe implements PipeTransform {
  private readonly currency = inject(CurrencySettings);

  transform(value: number | null | undefined, compact = false, currency?: string): string {
    if (value == null || !Number.isFinite(value)) return '–';
    return this.currency.format(value, { compact, currency });
  }
}
