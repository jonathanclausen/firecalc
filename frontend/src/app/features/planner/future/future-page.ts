import { httpResource } from '@angular/common/http';
import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Dashboard, Home, PlannerApi, Scenario, today } from '../../../core/api/planner-api';
import { Auth } from '../../../core/auth/auth';
import {
  ProjectionPoint,
  ageReaching,
  inTodaysMoney,
  pointAtAge,
  project,
} from '../../../core/finance/projection';
import { I18n } from '../../../core/i18n/i18n';
import { CurrencySettings } from '../../../core/settings/currency';
import { readStorage, writeStorage } from '../../../core/storage';
import { MoneyPipe } from '../../../shared/money.pipe';
import { INCLUDE_HOME_KEY } from '../dashboard/dashboard-page';
import { LineChart, LineSeries } from '../portfolio/history/line-chart';
import { CurrentCourse } from './current-course';
import { SCENARIO_KEYS, startingPoint, yearly } from './future-data';

const TODAYS_MONEY_KEY = 'firecalc.todaysMoney';
const END_AGE = 90;

interface Row {
  scenario: Scenario;
  key: string;
  shown: boolean;
  points: ProjectionPoint[];
  in5: number | null;
  in10: number | null;
  atFire: number | null;
  firstWithdrawal: number | null;
  at90: number | null;
  depletedAge: number | null;
  goalAge: number | null;
}

/** Saved scenarios projected from today's net worth, side by side. */
@Component({
  selector: 'app-future-page',
  imports: [RouterLink, MoneyPipe, LineChart, CurrentCourse],
  templateUrl: './future-page.html',
  styleUrl: './future-page.scss',
})
export class FuturePage {
  protected readonly i18n = inject(I18n);
  protected readonly auth = inject(Auth);
  private readonly api = inject(PlannerApi);
  private readonly currencySettings = inject(CurrencySettings);

  protected readonly includeHome = signal(readStorage(INCLUDE_HOME_KEY) !== 'false');
  protected readonly todaysMoney = signal(readStorage(TODAYS_MONEY_KEY) === 'true');

  protected readonly dashboard = httpResource<Dashboard>(() =>
    this.includeHome() ? '/api/dashboard' : '/api/dashboard?includeHome=false',
  );
  /** Keeps the previous dashboard on screen while the switch reloads it. */
  protected readonly view = linkedSignal<Dashboard | undefined, Dashboard | undefined>({
    source: () => this.dashboard.value(),
    computation: (value, previous) => value ?? previous?.value,
  });
  /** The "keep going as now" projection needs something to start from. */
  protected readonly course = computed(() => (this.view()?.latest ? this.view()! : null));
  protected readonly scenarios = httpResource<Scenario[]>(() => '/api/scenarios');

  private readonly homes = httpResource<Home[]>(() => '/api/homes');
  protected readonly hasHome = computed(() => (this.homes.value() ?? []).length > 0);

  protected readonly birthDate = computed(() => this.auth.user()?.birthDate ?? null);
  protected readonly editingBirth = signal(false);
  protected readonly birthInput = signal('');
  protected readonly savingBirth = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Scenario ids hidden from the chart; all are shown by default. */
  protected readonly hidden = signal<ReadonlySet<string>>(new Set());

  protected readonly loading = computed(() => !this.view() || !this.scenarios.hasValue());
  protected readonly failed = computed(() => !!(this.dashboard.error() || this.scenarios.error()));
  protected readonly currency = computed(() => this.view()?.currency ?? 'DKK');
  protected readonly goalTarget = computed(() => this.view()?.goal?.goal.targetAmount ?? null);

  protected readonly rows = computed<Row[]>(() => {
    const dash = this.view();
    const birth = this.birthDate();
    const list = this.scenarios.value() ?? [];
    if (!dash || !birth) return [];
    const start = startingPoint(dash);
    const now = today();
    const target = this.goalTarget();
    return list.map((scenario, i) => {
      const projection = project(start, scenario, birth, now, END_AGE);
      const points = this.todaysMoney()
        ? inTodaysMoney(projection.points, scenario.inflationPct)
        : projection.points;
      const first = points[0];
      const at = (age: number) => pointAtAge(points, age)?.netWorth ?? null;
      return {
        scenario,
        key: SCENARIO_KEYS[i % SCENARIO_KEYS.length],
        shown: !this.hidden().has(scenario.id),
        points,
        in5: at(first.age + 5),
        in10: at(first.age + 10),
        atFire: at(scenario.fireAge),
        firstWithdrawal: points.find((p) => p.age > scenario.fireAge)?.withdrawal ?? null,
        at90: at(END_AGE),
        depletedAge: projection.depletedAge,
        goalAge: target === null ? null : ageReaching(points, target),
      };
    });
  });

  /** The chart's years, from the first scenario (all start today and run to the same age). */
  private readonly chartPoints = computed(() => yearly(this.rows()[0]?.points ?? []));
  protected readonly dates = computed(() => this.chartPoints().map((p) => p.date));
  protected readonly series = computed<LineSeries[]>(() =>
    this.rows()
      .filter((r) => r.shown)
      .map((r) => ({
        key: r.key,
        label: r.scenario.name,
        values: yearly(r.points).map((p) => p.netWorth),
      })),
  );

  protected readonly xLabel = (index: number, short: boolean) => {
    const p = this.chartPoints()[index];
    if (!p) return '';
    const age = Math.floor(p.age + 1e-6);
    return short
      ? this.i18n.t().planner.futurePage.age(age)
      : this.i18n.t().planner.futurePage.yearAge(Number(p.date.slice(0, 4)), age);
  };

  protected readonly formatMoney = (value: number, compact: boolean) =>
    this.currencySettings.format(value, { compact, currency: this.currency() });

  protected ageText(age: number) {
    return this.i18n.t().planner.futurePage.age(Math.floor(age + 1e-6));
  }

  protected toggle(id: string) {
    const next = new Set(this.hidden());
    if (!next.delete(id)) next.add(id);
    this.hidden.set(next);
  }

  protected setIncludeHome(on: boolean) {
    this.includeHome.set(on);
    writeStorage(INCLUDE_HOME_KEY, String(on));
  }

  protected setTodaysMoney(on: boolean) {
    this.todaysMoney.set(on);
    writeStorage(TODAYS_MONEY_KEY, String(on));
  }

  protected editBirth() {
    this.birthInput.set(this.birthDate() ?? '');
    this.editingBirth.set(true);
  }

  protected async saveBirth(event: Event) {
    event.preventDefault();
    const value = this.birthInput();
    if (!value) return;
    this.savingBirth.set(true);
    this.error.set(null);
    try {
      const me = await this.api.saveProfile({ birthDate: value });
      this.auth.user.set(me);
      this.editingBirth.set(false);
    } catch {
      this.error.set(this.i18n.t().planner.error);
    } finally {
      this.savingBirth.set(false);
    }
  }
}
