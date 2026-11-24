import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Auth } from '../../core/auth/auth';
import { calculateCompoundInterest } from '../../core/finance/compound-interest';
import { I18n } from '../../core/i18n/i18n';
import { Translations } from '../../core/i18n/translations';
import { MoneyPipe } from '../../shared/money.pipe';
import { LangSwitch } from '../../shared/lang-switch';
import { parseDecimal } from '../../shared/parse-decimal';
import { Mascot } from './mascot';

/** Shown in the header and footer until the app gets its final name. */
export const BRAND = 'Snowball';

const RATE = 7;
const CHART_W = 1000;
const CHART_H = 360;
/** Number of snowballs riding the curve; each one is bigger than the last. */
const BALLS = 6;

/** Example equity share per year for the home tile, oldest first. */
const EQUITY = [22, 30, 38, 47, 57, 68, 80];

@Component({
  selector: 'app-landing-page',
  imports: [RouterLink, LangSwitch, Mascot, MoneyPipe],
  templateUrl: './landing-page.html',
  styleUrl: './landing-page.scss',
})
export class LandingPage {
  protected readonly i18n = inject(I18n);
  protected readonly auth = inject(Auth);
  protected readonly brand = BRAND;
  protected readonly equity = EQUITY;

  protected readonly start = signal(50_000);
  protected readonly monthly = signal(5_000);
  protected readonly years = signal(25);

  protected readonly result = computed(() =>
    calculateCompoundInterest({
      initialAmount: this.start(),
      contribution: this.monthly(),
      contributionChanges: [],
      contributionFrequency: 'monthly',
      contributionTiming: 'end',
      annualRate: RATE,
      compounding: 'monthly',
      years: this.years(),
      contributionGrowth: 0,
      inflation: 2,
    }),
  );

  /** Balance and deposits per year, starting with year 0. */
  private readonly points = computed(() => {
    const start = this.start();
    return [
      { year: 0, balance: start, deposits: start },
      ...this.result().rows.map((r) => ({
        year: r.year,
        balance: r.endBalance,
        deposits: r.totalContributions,
      })),
    ];
  });

  protected readonly chart = computed(() => {
    const pts = this.points();
    const years = this.years();
    const max = Math.max(1, pts[pts.length - 1].balance * 1.12);
    const x = (year: number) => (year / years) * CHART_W;
    const y = (v: number) => CHART_H - (v / max) * CHART_H;
    const line = (key: 'balance' | 'deposits') =>
      pts.map((p) => `${x(p.year).toFixed(1)},${y(p[key]).toFixed(1)}`).join(' L');
    const balances = line('balance');
    const balls = Array.from({ length: BALLS }, (_, i) => {
      const year = Math.round((years * (i + 1)) / BALLS);
      const size = 10 + (i + 1) ** 2 * 1.6;
      return {
        size,
        left: (x(year) / CHART_W) * 100,
        top: (y(pts[year].balance) / CHART_H) * 100,
      };
    });
    const crossover = this.result().crossoverYear;
    return {
      viewBox: `0 0 ${CHART_W} ${CHART_H}`,
      total: `M0,${CHART_H} L${balances} L${CHART_W},${CHART_H}Z`,
      deposits: `M0,${CHART_H} L${line('deposits')} L${CHART_W},${CHART_H}Z`,
      edge: `M${balances}`,
      balls,
      crossover:
        crossover && crossover < years
          ? { year: crossover, left: (crossover / years) * 100 }
          : null,
    };
  });

  protected readonly interestShare = computed(() => {
    const r = this.result();
    return r.finalBalance > 0 ? (r.totalInterest / r.finalBalance) * 100 : 0;
  });

  /** Ticker items twice over, so the loop has no gap. */
  protected readonly ticker = computed(() => [
    ...this.i18n.t().landing.ticker,
    ...this.i18n.t().landing.ticker,
  ]);

  constructor() {
    this.i18n.pageTitle.set((t: Translations) => t.landing.pageTitle);
    inject(DestroyRef).onDestroy(() => this.i18n.pageTitle.set((t) => t.pageTitle));
  }

  protected format(value: number) {
    return this.i18n.number(value);
  }

  protected update(field: 'start' | 'monthly' | 'years', raw: string) {
    const value = parseDecimal(raw, this.i18n.lang());
    if (value == null || value < 0) return;
    if (field === 'years') this.years.set(Math.min(60, Math.max(1, Math.round(value))));
    else this[field].set(Math.min(1e9, value));
  }

  /** Puts the cleaned-up value back in the field once the visitor leaves it. */
  protected tidy(input: HTMLInputElement, field: 'start' | 'monthly' | 'years') {
    input.value = this.format(this[field]());
  }
}
