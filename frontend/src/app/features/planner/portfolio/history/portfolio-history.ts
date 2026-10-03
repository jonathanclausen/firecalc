import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { PortfolioHistory as History } from '../../../../core/api/planner-api';
import { I18n } from '../../../../core/i18n/i18n';
import { CurrencySettings } from '../../../../core/settings/currency';
import { LineChart, LineSeries } from './line-chart';

type Range = 'm1' | 'm3' | 'ytd' | 'y1' | 'y5' | 'all' | 'custom';
const RANGES: Exclude<Range, 'custom'>[] = ['m1', 'm3', 'ytd', 'y1', 'y5', 'all'];

function iso(date: Date) {
  return date.toISOString().slice(0, 10);
}

/** Start date of a preset period ending today; null means since the first trade. */
function rangeStart(range: Exclude<Range, 'custom'>, today: Date): string | null {
  const d = new Date(today);
  switch (range) {
    case 'm1':
      d.setMonth(d.getMonth() - 1);
      break;
    case 'm3':
      d.setMonth(d.getMonth() - 3);
      break;
    case 'ytd':
      return `${today.getFullYear()}-01-01`;
    case 'y1':
      d.setFullYear(d.getFullYear() - 1);
      break;
    case 'y5':
      d.setFullYear(d.getFullYear() - 5);
      break;
    case 'all':
      return null;
  }
  return iso(d);
}

/** Combined return and value of all investment accounts over a chosen period. */
@Component({
  selector: 'app-portfolio-history',
  imports: [LineChart],
  templateUrl: './portfolio-history.html',
  styleUrl: './portfolio-history.scss',
})
export class PortfolioHistoryCard {
  protected readonly i18n = inject(I18n);
  private readonly currencySettings = inject(CurrencySettings);

  protected readonly ranges = RANGES;
  protected readonly today = iso(new Date());
  protected readonly range = signal<Range>('y1');
  /** Dates typed into the custom period fields. */
  protected readonly customFrom = signal<string>(rangeStart('y1', new Date())!);
  protected readonly customTo = signal<string>(this.today);

  private readonly period = computed(() => {
    const range = this.range();
    if (range === 'custom') return { from: this.customFrom() || null, to: this.customTo() || null };
    return { from: rangeStart(range, new Date()), to: null };
  });

  protected readonly history = httpResource<History>(() => {
    const { from, to } = this.period();
    const params: Record<string, string> = {};
    if (from) params['from'] = from;
    if (to) params['to'] = to;
    return { url: '/api/portfolio/history', params };
  });

  protected readonly points = computed(() => this.history.value()?.points ?? []);
  protected readonly dates = computed(() => this.points().map((p) => p.date));
  private readonly currency = computed(() => this.history.value()?.currency ?? 'DKK');

  protected readonly returnSeries = computed<LineSeries[]>(() => [
    {
      key: 'return',
      label: this.i18n.t().planner.portfolioPage.history.returnSeries,
      values: this.points().map((p) => p.returnPct),
    },
  ]);

  protected readonly valueSeries = computed<LineSeries[]>(() => {
    const h = this.i18n.t().planner.portfolioPage.history;
    return [
      { key: 'value', label: h.valueSeries, values: this.points().map((p) => p.value) },
      {
        key: 'deposits',
        label: h.depositsSeries,
        values: this.points().map((p) => p.netDeposits),
        dashed: true,
      },
    ];
  });

  /** Return, value now and the gain over the period (value change minus money put in). */
  protected readonly summary = computed(() => {
    const pts = this.points();
    if (pts.length === 0) return null;
    const first = pts[0];
    const last = pts[pts.length - 1];
    return {
      returnPct: last.returnPct,
      value: last.value,
      gain: last.value - last.netDeposits - (first.value - first.netDeposits),
    };
  });

  protected readonly formatPercent = (value: number) =>
    this.i18n.percent(value, Math.abs(value) < 10 ? 1 : 0);
  protected readonly formatMoney = (value: number, compact: boolean) =>
    this.currencySettings.format(value, { compact, currency: this.currency() });

  protected signedPercent(value: number) {
    return (value > 0 ? '+' : '') + this.i18n.percent(value, 1);
  }

  protected signedMoney(value: number) {
    return (value > 0 ? '+' : '') + this.formatMoney(value, false);
  }

  protected pick(range: Range) {
    if (range === 'custom') {
      // Start the custom fields from the period on screen.
      const h = this.history.value();
      if (h) {
        this.customFrom.set(h.from);
        this.customTo.set(h.to);
      }
    }
    this.range.set(range);
  }

  protected setFrom(value: string) {
    this.customFrom.set(value);
    this.range.set('custom');
  }

  protected setTo(value: string) {
    this.customTo.set(value);
    this.range.set('custom');
  }
}
