import { averageMonthlyChange, monthsBetween, requiredMonthlySaving } from './goal';

describe('goal maths', () => {
  it('counts months between dates', () => {
    expect(monthsBetween('2026-01-01', '2027-01-01')).toBeCloseTo(12, 1);
  });

  it('splits the gap evenly without returns', () => {
    expect(requiredMonthlySaving(100_000, 220_000, 12, 0)).toBeCloseTo(10_000, 6);
  });

  it('needs less per month when the money earns a return', () => {
    const saving = requiredMonthlySaving(0, 1_000_000, 120, 7)!;
    expect(saving).toBeLessThan(1_000_000 / 120);
    // Check by simulating the deposits month by month.
    const i = Math.pow(1.07, 1 / 12) - 1;
    let balance = 0;
    for (let m = 0; m < 120; m++) balance = balance * (1 + i) + saving;
    expect(balance).toBeCloseTo(1_000_000, 0);
  });

  it('needs nothing when growth alone reaches the target', () => {
    expect(requiredMonthlySaving(1_000_000, 1_500_000, 120, 7)).toBe(0);
  });

  it('has no answer when the date has passed', () => {
    expect(requiredMonthlySaving(0, 100, 0, 7)).toBeNull();
  });

  it('averages the monthly change across the history', () => {
    const series = [
      { date: '2026-01-01', total: 100_000 },
      { date: '2026-04-01', total: 120_000 },
      { date: '2027-01-01', total: 160_000 },
    ];
    expect(averageMonthlyChange(series)).toBeCloseTo(5_000, -1);
    expect(averageMonthlyChange(series.slice(0, 1))).toBeNull();
  });
});
