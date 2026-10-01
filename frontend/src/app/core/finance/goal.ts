/**
 * Goal maths for the planner. Plain TypeScript like the compound interest engine.
 * Dates are ISO yyyy-mm-dd strings.
 */

const DAYS_PER_MONTH = 365.2425 / 12;

/** Whole and fractional months between two ISO dates. */
export function monthsBetween(from: string, to: string): number {
  const ms = Date.parse(to) - Date.parse(from);
  return ms / 86_400_000 / DAYS_PER_MONTH;
}

/**
 * Monthly saving needed to grow `current` into `target` over `months`, at a constant
 * annual return (percent, compounded monthly at the equivalent effective rate).
 * Returns 0 when growth alone gets there, and null when there is no time left.
 */
export function requiredMonthlySaving(
  current: number,
  target: number,
  months: number,
  annualReturnPct: number,
): number | null {
  const n = Math.floor(months);
  if (n < 1) return null;
  const i = Math.pow(1 + annualReturnPct / 100, 1 / 12) - 1;
  const growth = Math.pow(1 + i, n);
  const shortfall = target - current * growth;
  if (shortfall <= 0) return 0;
  return i === 0 ? shortfall / n : (shortfall * i) / (growth - 1);
}

/**
 * Average change in net worth per month between the first and the latest snapshot.
 * Includes both deposits and market moves. Null until there are two snapshots a month apart.
 */
export function averageMonthlyChange(series: { date: string; total: number }[]): number | null {
  if (series.length < 2) return null;
  const first = series[0];
  const last = series[series.length - 1];
  const months = monthsBetween(first.date, last.date);
  return months >= 1 ? (last.total - first.total) / months : null;
}
