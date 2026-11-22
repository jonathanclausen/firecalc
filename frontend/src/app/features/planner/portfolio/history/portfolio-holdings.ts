import { Component, computed, inject, input, signal } from '@angular/core';
import { PortfolioHistory } from '../../../../core/api/planner-api';
import { I18n } from '../../../../core/i18n/i18n';
import { CurrencySettings } from '../../../../core/settings/currency';

interface Row {
  key: string;
  name: string;
  sub: string;
  value: number;
  gain: number;
  gainPct: number | null;
  sold: boolean;
}

/**
 * All securities across the investment accounts, each with how it did over the period picked in the
 * performance card. Accounts can be picked one at a time with the chips.
 */
@Component({
  selector: 'app-portfolio-holdings',
  templateUrl: './portfolio-holdings.html',
  styleUrl: './portfolio-holdings.scss',
})
export class PortfolioHoldings {
  protected readonly i18n = inject(I18n);
  private readonly currencySettings = inject(CurrencySettings);

  readonly history = input.required<PortfolioHistory>();
  /** The period's name, as shown in the column header (e.g. "1 år"). */
  readonly periodLabel = input.required<string>();
  readonly loading = input(false);

  /** The account shown, or null for all of them. */
  protected readonly account = signal<string | null>(null);

  protected readonly accounts = computed(() => this.history().accounts);

  /** The picked account, or all of them when it's gone (deleted, or not in this period). */
  protected readonly selected = computed(() => {
    const id = this.account();
    return id && this.accounts().some((a) => a.accountId === id) ? id : null;
  });

  protected readonly rows = computed<Row[]>(() => {
    const h = this.history();
    const only = this.selected();
    const names = new Map(h.accounts.map((a) => [a.accountId, a.name]));
    const groups = new Map<
      string,
      {
        name: string;
        symbol: string | null;
        qty: number;
        value: number;
        gain: number;
        invested: number;
        accounts: Set<string>;
      }
    >();
    for (const s of h.securities) {
      if (only && s.accountId !== only) continue;
      const g = groups.get(s.instrumentId) ?? {
        name: s.name,
        symbol: s.symbol,
        qty: 0,
        value: 0,
        gain: 0,
        invested: 0,
        accounts: new Set<string>(),
      };
      g.qty += s.quantity;
      g.value += s.value;
      g.gain += s.gain;
      g.invested += s.invested;
      if (s.quantity > 0 || s.gain !== 0) g.accounts.add(names.get(s.accountId) ?? '');
      groups.set(s.instrumentId, g);
    }
    const t = this.i18n.t().planner.portfolioPage.history;
    const rows: Row[] = [];
    for (const [key, g] of groups) {
      const sold = g.qty <= 0;
      // Sold before the period and untouched since: nothing to show.
      if (sold && Math.abs(g.gain) < 0.5) continue;
      const where = only || g.accounts.size === 0 ? g.symbol : [...g.accounts].join(' + ');
      const parts = [sold ? t.soldInPeriod : t.pieces(this.i18n.number(g.qty, 4)), where].filter(
        Boolean,
      );
      rows.push({
        key,
        name: g.name,
        sub: parts.join(' · '),
        value: g.value,
        gain: g.gain,
        gainPct: g.invested > 0 ? (g.gain / g.invested) * 100 : null,
        sold,
      });
    }
    // Holdings by size, then what was sold during the period by gain.
    return rows.sort((a, b) =>
      a.sold !== b.sold ? (a.sold ? 1 : -1) : a.sold ? b.gain - a.gain : b.value - a.value,
    );
  });

  /** The picked accounts' totals; what the securities don't explain is cash, interest and fees. */
  protected readonly totals = computed(() => {
    const only = this.selected();
    const list = this.accounts().filter((a) => !only || a.accountId === only);
    const value = list.reduce((s, a) => s + a.value, 0);
    const gain = list.reduce((s, a) => s + a.gain, 0);
    const rows = this.rows();
    const other = {
      value: value - rows.reduce((s, r) => s + r.value, 0),
      gain: gain - rows.reduce((s, r) => s + r.gain, 0),
    };
    const showOther = Math.abs(other.value) >= 1 || Math.abs(other.gain) >= 1;
    return { value, gain, other: showOther ? other : null };
  });

  protected money(value: number) {
    return this.currencySettings.format(value, { currency: this.history().currency });
  }

  protected signedMoney(value: number) {
    const rounded = Math.round(value);
    return (rounded > 0 ? '+' : '') + this.money(rounded === 0 ? 0 : value);
  }

  protected signedPct(value: number) {
    return (value > 0 ? '+' : '') + this.i18n.percent(value, 1);
  }

  protected tone(value: number) {
    return Math.round(value) > 0 ? 'up' : Math.round(value) < 0 ? 'down' : '';
  }
}
