import {
  ScenarioAssumptions,
  StartingPoint,
  ageOn,
  ageReaching,
  inTodaysMoney,
  pointAtAge,
  project,
} from './projection';

const start: StartingPoint = {
  investments: 500_000,
  savings: 100_000,
  homeValue: 4_000_000,
  homeLoan: 3_000_000,
  otherLoans: 200_000,
};

const flat: ScenarioAssumptions = {
  monthlySavings: 10_000,
  investmentReturnPct: 0,
  savingsReturnPct: 0,
  homeGrowthPct: 0,
  inflationPct: 0,
  fireAge: 60,
  yearlySpending: 120_000,
  events: [],
};

// Born 1996-01-01, so 30 on 2026-01-01.
const BIRTH = '1996-01-01';
const TODAY = '2026-01-01';

describe('projection', () => {
  it('works out the age with a fraction', () => {
    expect(ageOn(BIRTH, TODAY)).toBe(30);
    expect(ageOn(BIRTH, '2026-07-02')).toBeCloseTo(30.5, 2);
    expect(ageOn('1993-05-17', '2026-05-16')).toBeLessThan(33);
  });

  it('starts from today and counts loans against net worth', () => {
    const { points } = project(start, flat, BIRTH, TODAY, 40);
    expect(points[0].netWorth).toBe(500_000 + 100_000 + 4_000_000 - 3_000_000 - 200_000);
    expect(points[0].homeEquity).toBe(1_000_000);
    expect(points[0].loans).toBe(-200_000);
    expect(points.length).toBe(10 * 12 + 1);
  });

  it('adds monthly savings to investments until the FIRE age', () => {
    const { points } = project(start, { ...flat, fireAge: 35 }, BIRTH, TODAY, 40);
    const at35 = pointAtAge(points, 35)!;
    expect(at35.investments).toBeCloseTo(500_000 + 60 * 10_000, 0);
    // After FIRE, 10.000 a month comes out: savings first, then investments.
    const at36 = pointAtAge(points, 36)!;
    expect(at36.savings).toBe(0);
    expect(at36.liquid).toBeCloseTo(at35.liquid - 120_000, 0);
  });

  it('compounds returns monthly at the yearly rate', () => {
    const s = { ...flat, monthlySavings: 0, investmentReturnPct: 7, homeGrowthPct: 2 };
    const { points } = project(start, s, BIRTH, TODAY, 31);
    expect(points[12].investments).toBeCloseTo(535_000, 0);
    expect(points[12].homeEquity).toBeCloseTo(4_080_000 - 3_000_000, 0);
  });

  it('stops saving and spends during a break set by age', () => {
    const s: ScenarioAssumptions = { ...flat, events: [{ kind: 'break', age: 33, years: 1 }] };
    const { points } = project(start, s, BIRTH, TODAY, 40);
    const at33 = pointAtAge(points, 33)!;
    const at34 = pointAtAge(points, 34)!;
    expect(at34.liquid).toBeCloseTo(at33.liquid - 120_000, 0);
    // Saving starts again after the break.
    expect(pointAtAge(points, 35)!.liquid).toBeCloseTo(at34.liquid + 120_000, 0);
  });

  it('changes savings from an age and adds one-off amounts', () => {
    const s: ScenarioAssumptions = {
      ...flat,
      events: [
        { kind: 'savings', age: 32, amount: 20_000 },
        { kind: 'lumpSum', age: 33, amount: -50_000 },
      ],
    };
    const { points } = project(start, s, BIRTH, TODAY, 40);
    expect(pointAtAge(points, 32)!.liquid).toBe(600_000 + 24 * 10_000);
    expect(pointAtAge(points, 33)!.liquid).toBe(600_000 + 24 * 10_000 + 12 * 20_000 - 50_000);
  });

  it('reports when the money runs out', () => {
    const s = { ...flat, fireAge: 30, yearlySpending: 600_000 };
    const { depletedAge } = project(start, s, BIRTH, TODAY, 40);
    // 600.000 covers exactly 12 months of 50.000; the 13th month is short.
    expect(depletedAge).toBeCloseTo(31 + 1 / 12, 5);
  });

  it('raises spending with inflation and can show today’s money', () => {
    const s = { ...flat, monthlySavings: 0, investmentReturnPct: 2, inflationPct: 2 };
    const { points } = project(start, s, BIRTH, TODAY, 31);
    const real = inTodaysMoney(points, 2);
    expect(real[12].investments).toBeCloseTo(500_000, 0);
  });

  it('finds the age net worth reaches a target', () => {
    const { points } = project(start, flat, BIRTH, TODAY, 40);
    // 1.400.000 today, +10.000 a month: 1.500.000 after 10 months.
    expect(ageReaching(points, 1_500_000)).toBeCloseTo(30 + 10 / 12, 5);
    expect(ageReaching(points, 1e12)).toBeNull();
  });
});
