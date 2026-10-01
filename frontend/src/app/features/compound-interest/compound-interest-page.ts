import { Component, afterNextRender, computed, effect, inject, signal } from '@angular/core';
import {
  CompoundInterestInput,
  CompoundingFrequency,
  ContributionTiming,
  Frequency,
  calculateCompoundInterest,
} from '../../core/finance/compound-interest';
import { I18n } from '../../core/i18n/i18n';
import { CurrencySettings } from '../../core/settings/currency';
import { readJson, writeStorage } from '../../core/storage';
import { MoneyPipe } from '../../shared/money.pipe';
import { NumberField } from '../../shared/number-field';
import { GrowthChart } from './components/growth-chart';
import { ScheduleTable } from './components/schedule-table';

const STORAGE_KEY = 'firecalc.compound-interest.v1';

export const DEFAULT_INPUT: CompoundInterestInput = {
  initialAmount: 50_000,
  contribution: 5_000,
  contributionFrequency: 'monthly',
  contributionTiming: 'end',
  annualRate: 7,
  compounding: 'monthly',
  years: 25,
  contributionGrowth: 0,
  inflation: 2,
};

@Component({
  selector: 'app-compound-interest-page',
  imports: [NumberField, GrowthChart, ScheduleTable, MoneyPipe],
  templateUrl: './compound-interest-page.html',
  styleUrl: './compound-interest-page.scss',
})
export class CompoundInterestPage {
  protected readonly i18n = inject(I18n);
  private readonly currency = inject(CurrencySettings);
  protected readonly currencyCode = computed(() => this.currency.current().code);

  protected readonly initialAmount = signal(DEFAULT_INPUT.initialAmount);
  protected readonly contribution = signal(DEFAULT_INPUT.contribution);
  protected readonly contributionFrequency = signal<Frequency>(DEFAULT_INPUT.contributionFrequency);
  protected readonly contributionTiming = signal<ContributionTiming>(
    DEFAULT_INPUT.contributionTiming,
  );
  protected readonly annualRate = signal(DEFAULT_INPUT.annualRate);
  protected readonly compounding = signal<CompoundingFrequency>(DEFAULT_INPUT.compounding);
  protected readonly years = signal(DEFAULT_INPUT.years);
  protected readonly contributionGrowth = signal(DEFAULT_INPUT.contributionGrowth);
  protected readonly inflation = signal(DEFAULT_INPUT.inflation);

  protected readonly frequencies: Frequency[] = [
    'monthly',
    'quarterly',
    'semiannually',
    'annually',
  ];
  protected readonly compoundings: CompoundingFrequency[] = [
    'daily',
    'monthly',
    'quarterly',
    'semiannually',
    'annually',
  ];

  protected readonly input = computed<CompoundInterestInput>(() => ({
    initialAmount: this.initialAmount(),
    contribution: this.contribution(),
    contributionFrequency: this.contributionFrequency(),
    contributionTiming: this.contributionTiming(),
    annualRate: this.annualRate(),
    compounding: this.compounding(),
    years: this.years(),
    contributionGrowth: this.contributionGrowth(),
    inflation: this.inflation(),
  }));

  protected readonly result = computed(() => calculateCompoundInterest(this.input()));

  protected readonly interestShare = computed(() => {
    const r = this.result();
    return r.finalBalance > 0 ? (r.totalInterest / r.finalBalance) * 100 : 0;
  });

  protected readonly growthMultiple = computed(() => {
    const r = this.result();
    return r.totalContributions > 0 ? r.finalBalance / r.totalContributions : 0;
  });

  protected readonly showSchedule = signal(false);

  /** True once saved inputs are restored; until then nothing is written back. */
  private readonly restored = signal(false);

  constructor() {
    // Render with defaults on the server and during hydration, then restore the
    // visitor's saved inputs in the browser so server and client markup match.
    afterNextRender(() => {
      this.apply({ ...DEFAULT_INPUT, ...readJson<CompoundInterestInput>(STORAGE_KEY) });
      this.restored.set(true);
    });
    effect(() => {
      const input = this.input();
      if (this.restored()) writeStorage(STORAGE_KEY, JSON.stringify(input));
    });
  }

  protected reset() {
    this.apply(DEFAULT_INPUT);
  }

  private apply(input: CompoundInterestInput) {
    this.initialAmount.set(input.initialAmount);
    this.contribution.set(input.contribution);
    this.contributionFrequency.set(input.contributionFrequency);
    this.contributionTiming.set(input.contributionTiming);
    this.annualRate.set(input.annualRate);
    this.compounding.set(input.compounding);
    this.years.set(input.years);
    this.contributionGrowth.set(input.contributionGrowth);
    this.inflation.set(input.inflation);
  }
}
