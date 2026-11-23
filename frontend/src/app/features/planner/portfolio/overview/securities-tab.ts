import { Component, PLATFORM_ID, computed, inject, output, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { I18n } from '../../../../core/i18n/i18n';
import { CurrencySettings } from '../../../../core/settings/currency';
import { PeriodPicker } from './period-picker';
import { PortfolioPeriod, SecurityRow, periodTotals, securityRows } from './portfolio-period';
import { SecurityRowView } from './security-row';
import { squarify } from './treemap';

/** Returns past these (in %) get the strongest colour on the heat map. */
const MAX_UP = 40;
const MAX_DOWN = 25;

/** The "Papirer" tab: every security across accounts for the period, as a list or a heat map. */
@Component({
  selector: 'app-securities-tab',
  imports: [PeriodPicker, SecurityRowView],
  templateUrl: './securities-tab.html',
  styleUrl: './securities-tab.scss',
})
export class SecuritiesTab {
  protected readonly i18n = inject(I18n);
  private readonly currencySettings = inject(CurrencySettings);
  protected readonly period = inject(PortfolioPeriod);

  readonly open = output<string>();

  protected readonly view = signal<'list' | 'map'>('list');
  /** The account shown, or null for all of them. */
  protected readonly account = signal<string | null>(null);

  protected readonly history = computed(() => this.period.history.value() ?? null);
  protected readonly currency = computed(() => this.history()?.currency ?? 'DKK');
  protected readonly accounts = computed(() => this.history()?.accounts ?? []);
  /** The picked account, or all of them when it's gone. */
  protected readonly selected = computed(() => {
    const id = this.account();
    return id && this.accounts().some((a) => a.accountId === id) ? id : null;
  });

  protected readonly rows = computed(() => {
    const h = this.history();
    return h ? securityRows(h, this.selected()) : [];
  });
  protected readonly held = computed(() => this.rows().filter((r) => !r.sold));
  protected readonly sold = computed(() => this.rows().filter((r) => r.sold));
  protected readonly totals = computed(() => {
    const h = this.history();
    return h ? periodTotals(h, this.selected(), this.rows()) : null;
  });

  /** Width over height of the heat map: tall on phones, wide on bigger screens. */
  protected readonly aspect = signal(
    isPlatformBrowser(inject(PLATFORM_ID)) && window.matchMedia('(min-width: 700px)').matches
      ? 2
      : 0.8,
  );
  protected readonly tiles = computed(() => squarify(this.held(), (r) => r.value, this.aspect()));

  protected tileColor(r: SecurityRow) {
    const pct = r.gainPct ?? 0;
    const t = pct >= 0 ? Math.min(1, pct / MAX_UP) : -Math.min(1, -pct / MAX_DOWN);
    // From a neutral grey towards green or red; strong colours get white text.
    const mix = Math.round(25 + Math.abs(t) * 75);
    const base = t >= 0 ? 'var(--positive)' : 'var(--danger)';
    return `color-mix(in srgb, ${base} ${mix}%, var(--surface-hover))`;
  }

  protected tileDark(r: SecurityRow) {
    const pct = r.gainPct ?? 0;
    return pct >= MAX_UP * 0.35 || pct <= -MAX_DOWN * 0.35;
  }

  protected pickAccount(value: string) {
    this.account.set(value || null);
  }

  protected money(value: number) {
    return this.currencySettings.format(value, { currency: this.currency() });
  }

  protected signedMoney(value: number) {
    const rounded = Math.round(value);
    return (rounded > 0 ? '+' : '') + this.money(rounded === 0 ? 0 : value);
  }

  protected signedPct(value: number) {
    return (value > 0 ? '+' : '') + this.i18n.percent(value, 1);
  }

  protected tone(value: number) {
    const v = Math.round(value);
    return v > 0 ? 'up' : v < 0 ? 'down' : '';
  }
}
