/**
 * A loan's payments as a Danish annuity loan paid monthly, the same steps as the API's MortgageMath: the
 * same ydelse each month until the end date, split into interest and afdrag; during afdragsfrihed only
 * interest is paid. Bidrag is a cost on what is owed and doesn't pay the loan down.
 */

export interface LoanTerms {
  interestPct: number | null;
  contributionPct?: number | null;
  endDate: string | null;
  interestOnlyUntil: string | null;
}

export interface MonthlyPayment {
  interest: number;
  contribution: number;
  repayment: number;
}

/** Whole months from one ISO date until another, at least 0. */
export function monthsBetween(from: string, to: string): number {
  const [fy, fm, fd] = from.split('-').map(Number);
  const [ty, tm, td] = to.split('-').map(Number);
  let months = (ty - fy) * 12 + tm - fm;
  if (td < fd) months--;
  return Math.max(0, months);
}

/** The payment for the month starting on `date`, with `owed` at its start. */
export function loanPayment(owed: number, terms: LoanTerms, date: string): MonthlyPayment {
  if (owed <= 0) return { interest: 0, contribution: 0, repayment: 0 };
  const contribution = (owed * (terms.contributionPct ?? 0)) / 1200;
  if (terms.interestPct === null || terms.endDate === null)
    return { interest: 0, contribution, repayment: 0 };

  const r = terms.interestPct / 1200;
  const interest = owed * r;
  if (terms.interestOnlyUntil && date < terms.interestOnlyUntil)
    return { interest, contribution, repayment: 0 };

  const left = monthsBetween(date, terms.endDate);
  if (left <= 1) return { interest, contribution, repayment: owed };
  const annuity = r === 0 ? owed / left : (owed * r) / (1 - Math.pow(1 + r, -left));
  return { interest, contribution, repayment: Math.min(owed, annuity - interest) };
}
