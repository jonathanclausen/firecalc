import { Component, computed, inject, input, output, signal } from '@angular/core';
import { Portfolio } from '../../../../core/api/planner-api';
import { I18n } from '../../../../core/i18n/i18n';
import { CurrencySettings } from '../../../../core/settings/currency';
import { LineChart, LineSeries } from '../history/line-chart';
import { PeriodPicker } from './period-picker';
import { PortfolioPeriod, securityRows } from './portfolio-period';
import { SecurityRowView } from './security-row';

/** The "Udvikling" tab: the value now, its chart over the period, key figures and the biggest movers. */
@Component({
  selector: 'app-development-tab',
  imports: [LineChart, PeriodPicker, SecurityRowView],
  templateUrl: './development-tab.html',
  styleUrl: './development-tab.scss',
})
export class DevelopmentTab {
  protected readonly i18n = inject(I18n);
  private readonly currencySettings = inject(CurrencySettings);
  protected readonly period = inject(PortfolioPeriod);

  readonly portfolio = input.required<Portfolio>();
  /** Totals across accounts, as on the page. */
  readonly unrealized = input(0);
  readonly cash = input(0);
  readonly pricesAsOf = input<string | null>(null);
  readonly open = output<string>();
  readonly seeAll = output<void>();

  protected readonly chart = signal<'value' | 'return'>('value');

  protected readonly currency = computed(() => this.portfolio().currency);
  private readonly points = computed(() => this.period.history.value()?.points ?? []);
  protected readonly dates = computed(() => this.points().map((p) => p.date));
  protected readonly series = computed<LineSeries[]>(() => {
    const h = this.i18n.t().planner.portfolioPage.history;
    return this.chart() === 'value'
      ? [{ key: 'value', label: h.valueSeries, values: this.points().map((p) => p.value) }]
      : [{ key: 'return', label: h.returnSeries, values: this.points().map((p) => p.returnPct) }];
  });
  protected readonly returnPct = computed(() => this.points().at(-1)?.returnPct ?? null);

  protected readonly rows = computed(() => {
    const h = this.period.history.value();
    return h ? securityRows(h, null) : [];
  });
  protected readonly dividends = computed(() => this.rows().reduce((s, r) => s + r.dividends, 0));
  /** The three securities that moved the most, up or down. */
  protected readonly movers = computed(() =>
    [...this.rows()].sort((a, b) => Math.abs(b.gain) - Math.abs(a.gain)).slice(0, 3),
  );

  protected money = (value: number, compact = false) =>
    this.currencySettings.format(value, { compact, currency: this.currency() });
  protected percent = (value: number) => this.i18n.percent(value, Math.abs(value) < 10 ? 1 : 0);

  protected signedMoney(value: number) {
    const rounded = Math.round(value);
    return (rounded > 0 ? '+' : '') + this.money(rounded === 0 ? 0 : value);
  }

  protected signedPct(value: number) {
    return (value > 0 ? '+' : '') + this.i18n.percent(value, 1);
  }

  protected tone(value: number | null) {
    if (value === null) return '';
    const v = Math.round(value * 10);
    return v > 0 ? 'up' : v < 0 ? 'down' : '';
  }
}
