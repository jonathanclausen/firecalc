import { LoanTerms, loanPayment } from './mortgage';

/**
 * Projects net worth month by month from today, for a scenario of assumptions and life events placed
 * by age. Plain TypeScript like the other engines. Dates are ISO yyyy-mm-dd strings.
 */

export type ScenarioEventKind = 'break' | 'savings' | 'lumpSum';

export interface ScenarioEvent {
  kind: ScenarioEventKind;
  /** Age the event starts at, e.g. 33 for "from your 33rd birthday". */
  age: number;
  /** A break's length in years. */
  years?: number | null;
  /** New monthly savings (savings) or a one-off amount, negative for money out (lumpSum). */
  amount?: number | null;
}

export interface ScenarioAssumptions {
  /** Added to investments each month until FIRE. */
  monthlySavings: number;
  /** Added to savings and cash each month until FIRE (negative takes from them). */
  monthlyToSavings?: number;
  investmentReturnPct: number;
  /** Return on savings and cash accounts. */
  savingsReturnPct: number;
  homeGrowthPct: number;
  inflationPct: number;
  /** Saving stops at this age and the withdrawal rate is taken out instead. */
  fireAge: number;
  /** Percent of investments and savings taken out each year after FIRE, e.g. 4. */
  withdrawalPct: number;
  /** Yearly spending in today's money during a break. */
  yearlySpending: number;
  events: ScenarioEvent[];
}

/** A loan on the home: what is owed today and the terms it is paid down by (flat without them). */
export interface HomeLoan extends LoanTerms {
  owed: number;
}

/** Today's values, split the way the projection grows them. Loans are amounts owed (positive). */
export interface StartingPoint {
  investments: number;
  savings: number;
  homeValue: number;
  homeLoans: HomeLoan[];
  /** Other loans, which stay as they are. */
  otherLoans: number;
}

export interface ProjectionPoint {
  date: string;
  age: number;
  investments: number;
  savings: number;
  homeEquity: number;
  /** What the home's loans owe (positive). */
  homeOwed: number;
  /** Other loans, negative. */
  loans: number;
  netWorth: number;
  /** Investments and savings: the money that can be spent. */
  liquid: number;
  /** What is being taken out per year at this point (after FIRE or during a break), else 0. */
  withdrawal: number;
}

export interface Projection {
  points: ProjectionPoint[];
  /** First age the liquid money ran out while spending, or null if it lasts. */
  depletedAge: number | null;
  /** Age the home's loans are all paid off, or null if they aren't within the projection (or there are none). */
  loansPaidAge: number | null;
}

/** Monthly rate equivalent to a yearly return in percent. */
function monthlyRate(annualPct: number) {
  return Math.pow(1 + annualPct / 100, 1 / 12) - 1;
}

/** Age in years (with a fraction) on a date. */
export function ageOn(birthDate: string, date: string): number {
  const b = new Date(birthDate + 'T00:00:00Z');
  const d = new Date(date + 'T00:00:00Z');
  let years = d.getUTCFullYear() - b.getUTCFullYear();
  const birthday = Date.UTC(d.getUTCFullYear(), b.getUTCMonth(), b.getUTCDate());
  if (d.getTime() < birthday) years--;
  const last = Date.UTC(b.getUTCFullYear() + years, b.getUTCMonth(), b.getUTCDate());
  const next = Date.UTC(b.getUTCFullYear() + years + 1, b.getUTCMonth(), b.getUTCDate());
  return years + (d.getTime() - last) / (next - last);
}

function addMonths(iso: string, months: number): string {
  const [y, m, d] = iso.split('-').map(Number);
  const date = new Date(Date.UTC(y, m - 1 + months, 1));
  const lastDay = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 0)).getUTCDate();
  date.setUTCDate(Math.min(d, lastDay));
  return date.toISOString().slice(0, 10);
}

const round = (v: number) => Math.round(v * 100) / 100;

/**
 * Runs the scenario from `today` until `endAge`. Each month the balances grow, then either the month's
 * savings go into investments or, after the FIRE age and during a break, the month's spending (raised
 * with inflation) comes out of savings first and then investments. The home's loans are paid down by their
 * terms (the payments come out of income, which the monthly savings are already net of); other loans stay.
 */
export function project(
  start: StartingPoint,
  s: ScenarioAssumptions,
  birthDate: string,
  today: string,
  endAge = 90,
): Projection {
  const startAge = ageOn(birthDate, today);
  const months = Math.max(0, Math.ceil((endAge - startAge) * 12));
  const ri = monthlyRate(s.investmentReturnPct);
  const rs = monthlyRate(s.savingsReturnPct);
  const rh = monthlyRate(s.homeGrowthPct);
  const inflation = 1 + s.inflationPct / 100;
  const savingsChanges = s.events.filter((e) => e.kind === 'savings').sort((a, b) => a.age - b.age);

  let investments = start.investments;
  let savings = start.savings;
  let home = start.homeValue;
  const owed = start.homeLoans.map((l) => l.owed);
  const homeOwed = () => owed.reduce((sum, o) => sum + o, 0);
  let loansPaidAge: number | null = null;
  let depletedAge: number | null = null;
  /** The yearly amount the withdrawal rate gives, set at FIRE and again each year after. */
  let yearlyWithdrawal = 0;
  let firstFireMonth: number | null = null;
  let withdrawing = 0;
  const points: ProjectionPoint[] = [];

  const push = (m: number, age: number) =>
    points.push({
      date: addMonths(today, m),
      age,
      investments: round(investments),
      savings: round(savings),
      homeEquity: round(home - homeOwed()),
      homeOwed: round(homeOwed()),
      loans: round(-start.otherLoans),
      netWorth: round(investments + savings + home - homeOwed() - start.otherLoans),
      liquid: round(investments + savings),
      withdrawal: round(withdrawing),
    });

  push(0, startAge);
  for (let m = 1; m <= months; m++) {
    const prevAge = startAge + (m - 1) / 12;
    const age = startAge + m / 12;
    investments *= 1 + ri;
    savings *= 1 + rs;
    home *= 1 + rh;
    const monthStart = addMonths(today, m - 1);
    start.homeLoans.forEach((loan, i) => {
      owed[i] = Math.max(0, owed[i] - loanPayment(owed[i], loan, monthStart).repayment);
    });
    if (loansPaidAge === null && start.homeLoans.length && homeOwed() < 0.005) loansPaidAge = age;

    const onBreak = s.events.some(
      (e) => e.kind === 'break' && age > e.age && age <= e.age + (e.years ?? 0),
    );
    let flow: number;
    if (age > s.fireAge) {
      // The 4 % rule style: a share of what's left, worked out once a year.
      firstFireMonth ??= m;
      if ((m - firstFireMonth) % 12 === 0)
        yearlyWithdrawal = (Math.max(investments + savings, 0) * s.withdrawalPct) / 100;
      withdrawing = yearlyWithdrawal;
      flow = -yearlyWithdrawal / 12;
    } else if (onBreak) {
      withdrawing = s.yearlySpending * Math.pow(inflation, m / 12);
      flow = -withdrawing / 12;
    } else {
      withdrawing = 0;
      const change = savingsChanges.filter((e) => e.age < age).pop();
      flow = change ? (change.amount ?? 0) : s.monthlySavings;
      savings = Math.max(0, savings + (s.monthlyToSavings ?? 0));
    }
    for (const e of s.events)
      if (e.kind === 'lumpSum' && e.age > prevAge && e.age <= age) flow += e.amount ?? 0;

    if (flow >= 0) investments += flow;
    else {
      let need = -flow;
      const fromSavings = Math.min(Math.max(savings, 0), need);
      savings -= fromSavings;
      need -= fromSavings;
      const fromInvestments = Math.min(Math.max(investments, 0), need);
      investments -= fromInvestments;
      need -= fromInvestments;
      if (need > 0.005 && depletedAge === null) depletedAge = age;
    }
    push(m, age);
  }
  return { points, depletedAge, loansPaidAge };
}

/** The same projection in today's money: each value divided by inflation since today. */
export function inTodaysMoney(points: ProjectionPoint[], inflationPct: number): ProjectionPoint[] {
  const inflation = 1 + inflationPct / 100;
  return points.map((p, m) => {
    const f = Math.pow(inflation, m / 12);
    return {
      ...p,
      investments: round(p.investments / f),
      savings: round(p.savings / f),
      homeEquity: round(p.homeEquity / f),
      homeOwed: round(p.homeOwed / f),
      loans: round(p.loans / f),
      netWorth: round(p.netWorth / f),
      liquid: round(p.liquid / f),
      withdrawal: round(p.withdrawal / f),
    };
  });
}

/** The point closest to an age, or null when the age is outside the projection. */
export function pointAtAge(points: ProjectionPoint[], age: number): ProjectionPoint | null {
  if (!points.length || age < points[0].age - 1e-9 || age > points[points.length - 1].age + 1 / 24)
    return null;
  return points.reduce((best, p) => (Math.abs(p.age - age) < Math.abs(best.age - age) ? p : best));
}

/** First age net worth reaches the target, or null if it never does. */
export function ageReaching(points: ProjectionPoint[], target: number): number | null {
  return points.find((p) => p.netWorth >= target)?.age ?? null;
}
