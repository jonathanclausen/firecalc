import { loanPayment, monthsBetween } from './mortgage';

describe('mortgage', () => {
  const terms = {
    interestPct: 4,
    contributionPct: 0.5,
    endDate: '2056-01-01',
    interestOnlyUntil: null,
  };

  it('pays the same ydelse each month, split into interest and afdrag', () => {
    const first = loanPayment(1_200_000, terms, '2026-01-01');
    expect(first.interest).toBeCloseTo(4_000, 6);
    expect(first.contribution).toBeCloseTo(500, 6);
    expect(first.interest + first.repayment).toBeCloseTo(5_728.98, 1);
    const owed = 1_200_000 - first.repayment;
    const second = loanPayment(owed, terms, '2026-02-01');
    expect(second.interest + second.repayment).toBeCloseTo(first.interest + first.repayment, 6);
  });

  it('pays only interest during afdragsfrihed and the rest in the last month', () => {
    const io = { ...terms, interestOnlyUntil: '2027-01-01' };
    expect(loanPayment(1_000_000, io, '2026-12-01').repayment).toBe(0);
    expect(loanPayment(1_000_000, io, '2027-01-01').repayment).toBeGreaterThan(0);
    expect(loanPayment(10_000, terms, '2055-12-15').repayment).toBe(10_000);
  });

  it('stays put without terms', () => {
    const none = { interestPct: null, endDate: null, interestOnlyUntil: null };
    expect(loanPayment(1_000_000, none, '2026-01-01').repayment).toBe(0);
  });

  it('counts whole months', () => {
    expect(monthsBetween('2026-01-15', '2026-03-14')).toBe(1);
    expect(monthsBetween('2026-01-15', '2026-03-15')).toBe(2);
    expect(monthsBetween('2026-03-15', '2026-01-15')).toBe(0);
  });
});
