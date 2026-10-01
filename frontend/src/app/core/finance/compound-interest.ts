/**
 * Pure compound interest engine. No Angular dependencies so it can be unit tested in
 * isolation and later mirrored (or replaced) by the .NET backend.
 */

export type Frequency = 'monthly' | 'quarterly' | 'semiannually' | 'annually';
export type CompoundingFrequency = Frequency | 'daily';
export type ContributionTiming = 'start' | 'end';

export const PERIODS_PER_YEAR: Record<CompoundingFrequency, number> = {
  daily: 365,
  monthly: 12,
  quarterly: 4,
  semiannually: 2,
  annually: 1,
};

export interface CompoundInterestInput {
  /** Starting balance. */
  initialAmount: number;
  /** Recurring deposit per contribution period (negative values are treated as 0). */
  contribution: number;
  contributionFrequency: Frequency;
  /** Whether deposits land at the start or end of each contribution period. */
  contributionTiming: ContributionTiming;
  /** Nominal annual return in percent, e.g. 7 for 7%. */
  annualRate: number;
  compounding: CompoundingFrequency;
  /** Investment horizon in whole years. */
  years: number;
  /** Yearly increase of the recurring deposit in percent (e.g. salary growth). */
  contributionGrowth: number;
  /** Expected yearly inflation in percent, used for the real (today's money) value. */
  inflation: number;
}

export interface YearRow {
  year: number;
  startBalance: number;
  contributions: number;
  interest: number;
  endBalance: number;
  /** Cumulative deposits including the initial amount. */
  totalContributions: number;
  totalInterest: number;
  /** End balance expressed in today's money. */
  realEndBalance: number;
}

export interface CompoundInterestResult {
  rows: YearRow[];
  finalBalance: number;
  totalContributions: number;
  totalInterest: number;
  realFinalBalance: number;
  /** First year where interest earned exceeds that year's deposits, if any. */
  crossoverYear: number | null;
  /** Years needed to double money at the given rate (exact, not the rule of 72). */
  doublingYears: number | null;
}

const MONTHS_PER_YEAR = 12;

/** Effective monthly rate that matches the nominal annual rate at the given compounding. */
export function effectiveMonthlyRate(annualRatePct: number, compounding: CompoundingFrequency) {
  const n = PERIODS_PER_YEAR[compounding];
  return Math.pow(1 + annualRatePct / 100 / n, n / MONTHS_PER_YEAR) - 1;
}

export function calculateCompoundInterest(input: CompoundInterestInput): CompoundInterestResult {
  const years = Math.max(0, Math.floor(input.years));
  const initial = Math.max(0, input.initialAmount);
  const baseContribution = Math.max(0, input.contribution);
  const monthlyRate = effectiveMonthlyRate(input.annualRate, input.compounding);
  const monthsPerContribution = MONTHS_PER_YEAR / PERIODS_PER_YEAR[input.contributionFrequency];
  const growth = input.contributionGrowth / 100;
  const inflation = input.inflation / 100;

  const rows: YearRow[] = [];
  let balance = initial;
  let totalContributions = initial;
  let totalInterest = 0;
  let crossoverYear: number | null = null;

  for (let year = 1; year <= years; year++) {
    const startBalance = balance;
    const deposit = baseContribution * Math.pow(1 + growth, year - 1);
    let contributions = 0;
    let interest = 0;

    for (let month = 0; month < MONTHS_PER_YEAR; month++) {
      const isStartOfPeriod = month % monthsPerContribution === 0;
      const isEndOfPeriod = (month + 1) % monthsPerContribution === 0;

      if (input.contributionTiming === 'start' && isStartOfPeriod) {
        balance += deposit;
        contributions += deposit;
      }

      const earned = balance * monthlyRate;
      balance += earned;
      interest += earned;

      if (input.contributionTiming === 'end' && isEndOfPeriod) {
        balance += deposit;
        contributions += deposit;
      }
    }

    totalContributions += contributions;
    totalInterest += interest;
    if (crossoverYear === null && contributions > 0 && interest > contributions) {
      crossoverYear = year;
    }

    rows.push({
      year,
      startBalance,
      contributions,
      interest,
      endBalance: balance,
      totalContributions,
      totalInterest,
      realEndBalance: balance / Math.pow(1 + inflation, year),
    });
  }

  const annualEffective = Math.pow(1 + monthlyRate, MONTHS_PER_YEAR) - 1;

  return {
    rows,
    finalBalance: balance,
    totalContributions,
    totalInterest,
    realFinalBalance: balance / Math.pow(1 + inflation, years),
    crossoverYear,
    doublingYears: annualEffective > 0 ? Math.log(2) / Math.log(1 + annualEffective) : null,
  };
}
