import { httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  Dashboard,
  PlannerApi,
  SaveScenario,
  Scenario,
  today,
} from '../../../core/api/planner-api';
import { Auth } from '../../../core/auth/auth';
import {
  ScenarioEvent,
  ScenarioEventKind,
  pointAtAge,
  project,
} from '../../../core/finance/projection';
import { I18n } from '../../../core/i18n/i18n';
import { CurrencySettings } from '../../../core/settings/currency';
import { readStorage } from '../../../core/storage';
import { DecimalInput } from '../../../shared/decimal-input';
import { formatDecimal, parseDecimal } from '../../../shared/parse-decimal';
import { MoneyPipe } from '../../../shared/money.pipe';
import { INCLUDE_HOME_KEY } from '../dashboard/dashboard-page';
import { LineChart, LineSeries } from '../portfolio/history/line-chart';
import { startingPoint, yearly } from '../future/future-data';

const END_AGE = 90;

interface EventDraft {
  kind: ScenarioEventKind;
  age: string;
  years: string;
  amount: string;
}

type NumberField =
  | 'monthlySavings'
  | 'fireAge'
  | 'withdrawalPct'
  | 'yearlySpending'
  | 'investmentReturnPct'
  | 'savingsReturnPct'
  | 'homeGrowthPct'
  | 'inflationPct';

const DEFAULTS: Record<NumberField, number> = {
  monthlySavings: 5000,
  fireAge: 50,
  withdrawalPct: 4,
  yearlySpending: 300_000,
  investmentReturnPct: 7,
  savingsReturnPct: 1,
  homeGrowthPct: 2,
  inflationPct: 2,
};

/** Creates or edits one scenario, with a live preview of its projection. */
@Component({
  selector: 'app-scenario-page',
  imports: [RouterLink, DecimalInput, MoneyPipe, LineChart],
  templateUrl: './scenario-page.html',
  styleUrl: './scenario-page.scss',
})
export class ScenarioPage {
  /** Route parameter: a scenario id, or "new". */
  readonly id = input.required<string>();
  /** Query parameter on "new": a scenario to start from. */
  readonly from = input<string>();

  protected readonly i18n = inject(I18n);
  private readonly api = inject(PlannerApi);
  private readonly router = inject(Router);
  private readonly auth = inject(Auth);
  private readonly currencySettings = inject(CurrencySettings);

  protected readonly isNew = computed(() => this.id() === 'new');
  protected readonly scenarios = httpResource<Scenario[]>(() => '/api/scenarios');
  private readonly dashboard = httpResource<Dashboard>(() =>
    readStorage(INCLUDE_HOME_KEY) !== 'false'
      ? '/api/dashboard'
      : '/api/dashboard?includeHome=false',
  );
  protected readonly currency = computed(() => this.dashboard.value()?.currency ?? 'DKK');
  protected readonly birthDate = computed(() => this.auth.user()?.birthDate ?? null);

  protected readonly existing = computed(() =>
    this.isNew() ? null : (this.scenarios.value()?.find((s) => s.id === this.id()) ?? null),
  );
  protected readonly notFound = computed(
    () => !this.isNew() && this.scenarios.hasValue() && !this.existing(),
  );

  protected readonly name = signal('');
  protected readonly fields: Record<NumberField, ReturnType<typeof signal<string>>> = {
    monthlySavings: signal(''),
    fireAge: signal(''),
    withdrawalPct: signal(''),
    yearlySpending: signal(''),
    investmentReturnPct: signal(''),
    savingsReturnPct: signal(''),
    homeGrowthPct: signal(''),
    inflationPct: signal(''),
  };
  protected readonly numberFields = computed(() => {
    const s = this.i18n.t().planner.scenarioPage;
    const cur = ` (${this.currency()})`;
    const fields: { key: NumberField; label: string; hint?: string }[] = [
      { key: 'monthlySavings', label: s.monthlySavings + cur, hint: s.monthlySavingsHint },
      { key: 'fireAge', label: s.fireAge, hint: s.fireAgeHint },
      { key: 'withdrawalPct', label: s.withdrawalPct, hint: s.withdrawalPctHint },
      { key: 'yearlySpending', label: s.yearlySpending + cur, hint: s.yearlySpendingHint },
      { key: 'investmentReturnPct', label: s.investmentReturn },
      { key: 'savingsReturnPct', label: s.savingsReturn },
      { key: 'homeGrowthPct', label: s.homeGrowth },
      { key: 'inflationPct', label: s.inflation },
    ];
    return fields;
  });
  protected readonly events = signal<EventDraft[]>([]);
  protected readonly kinds: ScenarioEventKind[] = ['break', 'savings', 'lumpSum'];

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    let filled = false;
    effect(() => {
      if (filled || !this.scenarios.hasValue()) return;
      filled = true;
      const lang = this.i18n.lang();
      const t = this.i18n.t().planner.scenarioPage;
      const source = this.isNew()
        ? (this.scenarios.value()?.find((s) => s.id === this.from()) ?? null)
        : this.existing();
      if (this.isNew()) this.name.set(source ? t.copyOf(source.name) : '');
      else if (source) this.name.set(source.name);
      for (const key of Object.keys(this.fields) as NumberField[])
        this.fields[key].set(formatDecimal(source ? source[key] : DEFAULTS[key], lang));
      this.events.set(
        (source?.events ?? []).map((e) => ({
          kind: e.kind,
          age: formatDecimal(e.age, lang),
          years: formatDecimal(e.years ?? null, lang),
          amount: formatDecimal(e.amount ?? null, lang),
        })),
      );
    });
  }

  /** The form as a scenario, or null while something is missing or not a number. */
  protected readonly parsed = computed<SaveScenario | null>(() => {
    const lang = this.i18n.lang();
    const num = (raw: string) => parseDecimal(raw, lang);
    const values = {} as Record<NumberField, number>;
    for (const key of Object.keys(this.fields) as NumberField[]) {
      const v = num(this.fields[key]());
      if (v === null) return null;
      values[key] = v;
    }
    const events: ScenarioEvent[] = [];
    for (const e of this.events()) {
      const age = num(e.age);
      if (age === null) return null;
      if (e.kind === 'break') {
        const years = num(e.years);
        if (years === null || years <= 0) return null;
        events.push({ kind: e.kind, age, years });
      } else {
        const amount = num(e.amount);
        if (amount === null || (e.kind === 'savings' && amount < 0)) return null;
        events.push({ kind: e.kind, age, amount });
      }
    }
    const name = this.name().trim();
    if (!name) return null;
    return { name, ...values, events };
  });

  /** The last scenario that parsed, so the preview doesn't blink while a number is half typed. */
  private lastPreview: SaveScenario | null = null;
  protected readonly preview = computed(() => {
    const parsed = this.parsed() ?? this.lastPreview;
    const dash = this.dashboard.value();
    const birth = this.birthDate();
    if (!parsed || !dash || !birth) return null;
    this.lastPreview = parsed;
    const projection = project(startingPoint(dash), parsed, birth, today(), END_AGE);
    const points = yearly(projection.points);
    const first = projection.points[0];
    const at = (age: number) => pointAtAge(projection.points, age)?.netWorth ?? null;
    return {
      points,
      dates: points.map((p) => p.date),
      series: [
        {
          key: 's0',
          label: this.i18n.t().planner.scenarioPage.netWorth,
          values: points.map((p) => p.netWorth),
        },
        {
          key: 's1',
          label: this.i18n.t().planner.scenarioPage.liquid,
          values: points.map((p) => p.liquid),
          dashed: true,
        },
      ] as LineSeries[],
      in10: at(first.age + 10),
      atFire: at(parsed.fireAge),
      firstWithdrawal: projection.points.find((p) => p.age > parsed.fireAge)?.withdrawal ?? null,
      fireAge: parsed.fireAge,
      depletedAge: projection.depletedAge,
    };
  });

  protected readonly xLabel = (index: number, short: boolean) => {
    const p = this.preview()?.points[index];
    if (!p) return '';
    const f = this.i18n.t().planner.futurePage;
    const age = Math.floor(p.age + 1e-6);
    return short ? f.age(age) : f.yearAge(Number(p.date.slice(0, 4)), age);
  };

  protected readonly formatMoney = (value: number, compact: boolean) =>
    this.currencySettings.format(value, { compact, currency: this.currency() });

  protected ageText(age: number) {
    return this.i18n.t().planner.futurePage.age(Math.floor(age + 1e-6));
  }

  protected addEvent() {
    const age = this.preview()?.points[0]?.age;
    const next = age === undefined ? '' : formatDecimal(Math.ceil(age + 1), this.i18n.lang());
    this.events.update((list) => [...list, { kind: 'break', age: next, years: '1', amount: '' }]);
  }

  protected updateEvent(index: number, change: Partial<EventDraft>) {
    this.events.update((list) => list.map((e, i) => (i === index ? { ...e, ...change } : e)));
  }

  protected removeEvent(index: number) {
    this.events.update((list) => list.filter((_, i) => i !== index));
  }

  protected async save(event: Event) {
    event.preventDefault();
    const body = this.parsed();
    if (!body) {
      this.error.set(this.i18n.t().planner.scenarioPage.invalid);
      return;
    }
    await this.run(() =>
      this.isNew() ? this.api.createScenario(body) : this.api.updateScenario(this.id(), body),
    );
  }

  protected async remove() {
    if (!confirm(this.i18n.t().planner.scenarioPage.confirmRemove)) return;
    await this.run(() => this.api.deleteScenario(this.id()));
  }

  private async run(action: () => Promise<unknown>) {
    this.saving.set(true);
    this.error.set(null);
    try {
      await action();
      await this.router.navigateByUrl('/planner/fire');
    } catch {
      this.error.set(this.i18n.t().planner.error);
    } finally {
      this.saving.set(false);
    }
  }
}
