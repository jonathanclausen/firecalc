import { Component, computed, effect, inject, signal } from '@angular/core';
import {
  CompoundInterestInput,
  CompoundingFrequency,
  ContributionTiming,
  Frequency,
  calculateCompoundInterest,
} from '../../core/finance/compound-interest';
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
  private readonly saved = { ...DEFAULT_INPUT, ...readJson<CompoundInterestInput>(STORAGE_KEY) };

  private readonly currency = inject(CurrencySettings);
  protected readonly currencyCode = computed(() => this.currency.current().code);

  protected readonly initialAmount = signal(this.saved.initialAmount);
  protected readonly contribution = signal(this.saved.contribution);
  protected readonly contributionFrequency = signal<Frequency>(this.saved.contributionFrequency);
  protected readonly contributionTiming = signal<ContributionTiming>(this.saved.contributionTiming);
  protected readonly annualRate = signal(this.saved.annualRate);
  protected readonly compounding = signal<CompoundingFrequency>(this.saved.compounding);
  protected readonly years = signal(this.saved.years);
  protected readonly contributionGrowth = signal(this.saved.contributionGrowth);
  protected readonly inflation = signal(this.saved.inflation);

  protected readonly frequencies: { value: Frequency; label: string; per: string }[] = [
    { value: 'monthly', label: 'Monthly', per: 'month' },
    { value: 'quarterly', label: 'Quarterly', per: 'quarter' },
    { value: 'semiannually', label: 'Half-yearly', per: 'half-year' },
    { value: 'annually', label: 'Yearly', per: 'year' },
  ];

  protected readonly compoundings: { value: CompoundingFrequency; label: string }[] = [
    { value: 'daily', label: 'Daily' },
    { value: 'monthly', label: 'Monthly' },
    { value: 'quarterly', label: 'Quarterly' },
    { value: 'semiannually', label: 'Half-yearly' },
    { value: 'annually', label: 'Yearly' },
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

  protected readonly contributionPer = computed(
    () => this.frequencies.find((f) => f.value === this.contributionFrequency())!.per,
  );

  protected readonly interestShare = computed(() => {
    const r = this.result();
    return r.finalBalance > 0 ? (r.totalInterest / r.finalBalance) * 100 : 0;
  });

  protected readonly growthMultiple = computed(() => {
    const r = this.result();
    return r.totalContributions > 0 ? r.finalBalance / r.totalContributions : 0;
  });

  protected readonly showSchedule = signal(false);

  constructor() {
    effect(() => writeStorage(STORAGE_KEY, JSON.stringify(this.input())));
  }

  protected reset() {
    this.initialAmount.set(DEFAULT_INPUT.initialAmount);
    this.contribution.set(DEFAULT_INPUT.contribution);
    this.contributionFrequency.set(DEFAULT_INPUT.contributionFrequency);
    this.contributionTiming.set(DEFAULT_INPUT.contributionTiming);
    this.annualRate.set(DEFAULT_INPUT.annualRate);
    this.compounding.set(DEFAULT_INPUT.compounding);
    this.years.set(DEFAULT_INPUT.years);
    this.contributionGrowth.set(DEFAULT_INPUT.contributionGrowth);
    this.inflation.set(DEFAULT_INPUT.inflation);
  }
}
