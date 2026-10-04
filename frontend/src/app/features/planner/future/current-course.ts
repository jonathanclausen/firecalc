import { Component, computed, inject, input, signal } from '@angular/core';
import { Dashboard, SeriesPoint, today } from '../../../core/api/planner-api';
import { project } from '../../../core/finance/projection';
import { I18n } from '../../../core/i18n/i18n';
import { CurrencySettings } from '../../../core/settings/currency';
import { readStorage, writeStorage } from '../../../core/storage';
import { DecimalInput } from '../../../shared/decimal-input';
import { MoneyPipe } from '../../../shared/money.pipe';
import { formatDecimal, parseDecimal } from '../../../shared/parse-decimal';
import { NetWorthChart } from '../dashboard/net-worth-chart';
import { startingPoint } from './future-data';

const RATES_KEY = 'firecalc.courseRates';
const YEARS = [1, 2, 5, 10];
const DEFAULT_RATES = { investmentReturnPct: 7, savingsReturnPct: 1, homeGrowthPct: 2 };
type RateKey = keyof typeof DEFAULT_RATES;

/**
 * "If you keep going as now": today's net worth carried forward 10 years with the last year's pace of
 * saving, so it needs no setup. Only the rates can be adjusted, and they are remembered on this device.
 */
@Component({
  selector: 'app-current-course',
  imports: [MoneyPipe, DecimalInput, NetWorthChart],
  templateUrl: './current-course.html',
  styleUrl: './current-course.scss',
})
export class CurrentCourse {
  readonly dashboard = input.required<Dashboard>();

  protected readonly i18n = inject(I18n);
  private readonly currencySettings = inject(CurrencySettings);

  protected readonly rates = signal<Record<RateKey, number>>(this.readRates());
  protected readonly rateFields = [
    { key: 'investmentReturnPct', label: 'investmentReturn' },
    { key: 'savingsReturnPct', label: 'savingsReturn' },
    { key: 'homeGrowthPct', label: 'homeGrowth' },
  ] as const;

  protected readonly currency = computed(() => this.dashboard().currency);
  protected readonly pace = computed(() => {
    const p = this.dashboard().pace;
    if (!p || (p.investedPerMonth === null && p.savedPerMonth === null)) return null;
    return { invested: p.investedPerMonth ?? 0, saved: p.savedPerMonth ?? 0 };
  });

  /** Month by month for 10 years; ages don't matter here, so the projection starts at age 0. */
  private readonly points = computed(() => {
    const now = today();
    const pace = this.pace();
    const { points } = project(
      startingPoint(this.dashboard()),
      {
        ...this.rates(),
        monthlySavings: pace?.invested ?? 0,
        monthlyToSavings: pace?.saved ?? 0,
        inflationPct: 0,
        fireAge: 1000,
        withdrawalPct: 0,
        yearlySpending: 0,
        events: [],
      },
      now,
      now,
      10,
    );
    return points;
  });

  protected readonly cards = computed(() => {
    const points = this.points();
    const start = points[0].netWorth;
    return YEARS.map((years) => {
      const p = points[Math.min(points.length - 1, years * 12)];
      return { years, value: p.netWorth, change: p.netWorth - start };
    });
  });

  /** The overview's history followed by the projection, a point a quarter, in the overview's shape. */
  protected readonly series = computed<SeriesPoint[]>(() => {
    const history = this.dashboard().series;
    const future = this.points()
      .filter((_, i) => i > 0 && i % 3 === 0)
      .map((p) => ({
        date: p.date,
        total: p.netWorth,
        byType: {
          investment: p.investments,
          savings: p.savings,
          ...(p.homeEquity !== 0 ? { property: p.homeEquity } : {}),
          ...(p.loans !== 0 ? { loan: p.loans } : {}),
        },
      }));
    return [...history, ...future];
  });

  protected readonly today = today();

  protected money(value: number) {
    return this.currencySettings.format(value, { currency: this.currency() });
  }

  protected signed(value: number) {
    const text = this.money(value);
    return value > 0 ? `+${text}` : text;
  }

  protected rateText(key: RateKey) {
    return formatDecimal(this.rates()[key], this.i18n.lang());
  }

  protected setRate(key: RateKey, raw: string) {
    const value = parseDecimal(raw, this.i18n.lang());
    if (value === null || value < -50 || value > 50) return;
    this.rates.update((r) => ({ ...r, [key]: value }));
    writeStorage(RATES_KEY, JSON.stringify(this.rates()));
  }

  private readRates(): Record<RateKey, number> {
    try {
      const saved = JSON.parse(readStorage(RATES_KEY) ?? '{}');
      return { ...DEFAULT_RATES, ...saved };
    } catch {
      return { ...DEFAULT_RATES };
    }
  }
}
