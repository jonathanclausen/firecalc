import { calculateCompoundInterest, CompoundInterestInput } from './compound-interest';

const base: CompoundInterestInput = {
  initialAmount: 0,
  contribution: 0,
  contributionFrequency: 'monthly',
  contributionTiming: 'end',
  annualRate: 0,
  compounding: 'monthly',
  years: 1,
  contributionGrowth: 0,
  inflation: 0,
};

describe('calculateCompoundInterest', () => {
  it('compounds a lump sum annually', () => {
    const r = calculateCompoundInterest({
      ...base,
      initialAmount: 1000,
      annualRate: 10,
      compounding: 'annually',
      years: 2,
    });
    expect(r.finalBalance).toBeCloseTo(1210, 6);
    expect(r.totalInterest).toBeCloseTo(210, 6);
    expect(r.rows.length).toBe(2);
  });

  it('compounds a lump sum monthly', () => {
    const r = calculateCompoundInterest({ ...base, initialAmount: 1000, annualRate: 12 });
    expect(r.finalBalance).toBeCloseTo(1000 * Math.pow(1.01, 12), 6);
  });

  it('matches the ordinary annuity formula for end-of-period deposits', () => {
    const r = calculateCompoundInterest({ ...base, contribution: 100, annualRate: 12 });
    expect(r.finalBalance).toBeCloseTo((100 * (Math.pow(1.01, 12) - 1)) / 0.01, 6);
    expect(r.totalContributions).toBeCloseTo(1200, 6);
  });

  it('matches the annuity-due formula for start-of-period deposits', () => {
    const r = calculateCompoundInterest({
      ...base,
      contribution: 100,
      annualRate: 12,
      contributionTiming: 'start',
    });
    expect(r.finalBalance).toBeCloseTo(((100 * (Math.pow(1.01, 12) - 1)) / 0.01) * 1.01, 6);
  });

  it('deposits quarterly', () => {
    const r = calculateCompoundInterest({
      ...base,
      contribution: 500,
      contributionFrequency: 'quarterly',
      years: 3,
    });
    expect(r.totalContributions).toBeCloseTo(6000, 6);
  });

  it('grows the deposit every year', () => {
    const r = calculateCompoundInterest({
      ...base,
      contribution: 100,
      contributionFrequency: 'annually',
      contributionGrowth: 10,
      years: 2,
    });
    expect(r.rows[1].contributions).toBeCloseTo(110, 6);
    expect(r.finalBalance).toBeCloseTo(210, 6);
  });

  it('discounts by inflation', () => {
    const r = calculateCompoundInterest({ ...base, initialAmount: 1000, inflation: 10 });
    expect(r.realFinalBalance).toBeCloseTo(1000 / 1.1, 6);
  });

  it('finds the crossover year and doubling time', () => {
    const r = calculateCompoundInterest({
      ...base,
      contribution: 1000,
      annualRate: 7,
      compounding: 'annually',
      years: 40,
    });
    expect(r.crossoverYear).not.toBeNull();
    const row = r.rows[r.crossoverYear! - 1];
    expect(row.interest).toBeGreaterThan(row.contributions);
    expect(r.rows[r.crossoverYear! - 2].interest).toBeLessThanOrEqual(
      r.rows[r.crossoverYear! - 2].contributions,
    );
    expect(r.doublingYears).toBeCloseTo(Math.log(2) / Math.log(1.07), 6);
  });

  it('handles a zero horizon', () => {
    const r = calculateCompoundInterest({ ...base, initialAmount: 500, years: 0 });
    expect(r.rows).toEqual([]);
    expect(r.finalBalance).toBe(500);
    expect(r.doublingYears).toBeNull();
  });
});

describe('contribution changes', () => {
  it('switches the deposit amount from the given year', () => {
    const r = calculateCompoundInterest({
      ...base,
      contribution: 1000,
      contributionChanges: [{ fromYear: 6, amount: 500 }],
      years: 10,
    });
    expect(r.rows[4].contributions).toBeCloseTo(12_000, 6);
    expect(r.rows[5].contributions).toBeCloseTo(6_000, 6);
    expect(r.totalContributions).toBeCloseTo(5 * 12_000 + 5 * 6_000, 6);
  });

  it('applies changes in year order regardless of list order and allows pausing', () => {
    const r = calculateCompoundInterest({
      ...base,
      contribution: 100,
      contributionFrequency: 'annually',
      contributionChanges: [
        { fromYear: 4, amount: 300 },
        { fromYear: 2, amount: 0 },
      ],
      years: 5,
    });
    expect(r.rows.map((row) => row.contributions)).toEqual([100, 0, 0, 300, 300]);
  });

  it('restarts yearly growth at the start of each period', () => {
    const r = calculateCompoundInterest({
      ...base,
      contribution: 100,
      contributionFrequency: 'annually',
      contributionGrowth: 10,
      contributionChanges: [{ fromYear: 3, amount: 200 }],
      years: 4,
    });
    const deposits = r.rows.map((row) => row.contributions);
    expect(deposits[1]).toBeCloseTo(110, 6);
    expect(deposits[2]).toBeCloseTo(200, 6);
    expect(deposits[3]).toBeCloseTo(220, 6);
  });
});
