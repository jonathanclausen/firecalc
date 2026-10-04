import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { I18n } from '../../../core/i18n/i18n';

/** Shapes from /api/admin/overview. Only counts, dates and yes/no; never amounts. */
export interface AdminSteps {
  account: boolean;
  import: boolean;
  goal: boolean;
  profile: boolean;
  scenario: boolean;
}

export interface AdminUser {
  id: string;
  email: string;
  name: string | null;
  createdAt: string;
  lastSeenAt: string | null;
  onboarded: boolean;
  hasDemo: boolean;
  steps: AdminSteps;
  stepsDone: number;
  accounts: number;
  homes: number;
  transactions: number;
  balances: number;
  scenarios: number;
}

export interface AdminOverview {
  totals: {
    users: number;
    newLast7Days: number;
    newLast30Days: number;
    activated: number;
    activeLast7Days: number;
  };
  signUpsByWeek: { weekStart: string; count: number }[];
  funnel: { key: string; count: number }[];
  users: AdminUser[];
}

type Filter = 'all' | 'notStarted' | 'started' | 'done';
type Sort = 'createdAt' | 'lastSeenAt' | 'stepsDone' | 'email';

/** Steps in the order of the "getting started" checklist. */
const STEP_KEYS: readonly (keyof AdminSteps)[] = [
  'account',
  'import',
  'goal',
  'profile',
  'scenario',
];
const PAGE_SIZE = 50;

/** Admins only: who has signed up, and how far they are with getting started. */
@Component({
  selector: 'app-admin-page',
  templateUrl: './admin-page.html',
  styleUrl: './admin-page.scss',
})
export class AdminPage {
  protected readonly i18n = inject(I18n);
  protected readonly overview = httpResource<AdminOverview>(() => '/api/admin/overview');
  protected readonly stepKeys = STEP_KEYS;
  protected readonly filters: readonly Filter[] = ['all', 'notStarted', 'started', 'done'];

  protected readonly filter = signal<Filter>('all');
  protected readonly search = signal('');
  protected readonly sort = signal<Sort>('createdAt');
  protected readonly descending = signal(true);
  protected readonly page = signal(0);

  protected readonly status = (u: AdminUser): Exclude<Filter, 'all'> =>
    u.stepsDone === 0 ? 'notStarted' : u.stepsDone === STEP_KEYS.length ? 'done' : 'started';

  protected readonly counts = computed(() => {
    const users = this.overview.value()?.users ?? [];
    const counts: Record<Filter, number> = {
      all: users.length,
      notStarted: 0,
      started: 0,
      done: 0,
    };
    for (const u of users) counts[this.status(u)]++;
    return counts;
  });

  protected readonly filtered = computed(() => {
    const query = this.search().trim().toLowerCase();
    const filter = this.filter();
    const key = this.sort();
    const dir = this.descending() ? -1 : 1;
    return (this.overview.value()?.users ?? [])
      .filter((u) => filter === 'all' || this.status(u) === filter)
      .filter(
        (u) =>
          !query ||
          u.email.toLowerCase().includes(query) ||
          (u.name ?? '').toLowerCase().includes(query),
      )
      .sort((a, b) => {
        const x = a[key] ?? '';
        const y = b[key] ?? '';
        return (x < y ? -1 : x > y ? 1 : 0) * dir;
      });
  });

  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.filtered().length / PAGE_SIZE)),
  );
  protected readonly shown = computed(() => {
    const start = Math.min(this.page(), this.pageCount() - 1) * PAGE_SIZE;
    return this.filtered().slice(start, start + PAGE_SIZE);
  });
  protected readonly range = computed(() => {
    const start = Math.min(this.page(), this.pageCount() - 1) * PAGE_SIZE;
    return { from: start + 1, to: start + this.shown().length, of: this.filtered().length };
  });

  protected readonly activatedPct = computed(() => {
    const t = this.overview.value()?.totals;
    return t && t.users ? Math.round((t.activated / t.users) * 100) : 0;
  });

  /** Bars for sign-ups per week, scaled to the busiest week. */
  protected readonly weeks = computed(() => {
    const weeks = this.overview.value()?.signUpsByWeek ?? [];
    const max = Math.max(1, ...weeks.map((w) => w.count));
    return weeks.map((w) => ({ ...w, height: (w.count / max) * 100 }));
  });

  protected readonly funnel = computed(() => {
    const steps = this.overview.value()?.funnel ?? [];
    const total = Math.max(1, steps[0]?.count ?? 0);
    return steps.map((s) => ({ ...s, width: (s.count / total) * 100 }));
  });

  protected setFilter(filter: Filter) {
    this.filter.set(filter);
    this.page.set(0);
  }

  protected setSearch(value: string) {
    this.search.set(value);
    this.page.set(0);
  }

  protected sortBy(key: Sort) {
    if (this.sort() === key) this.descending.update((d) => !d);
    else {
      this.sort.set(key);
      this.descending.set(key !== 'email');
    }
  }

  protected ariaSort(key: Sort) {
    if (this.sort() !== key) return null;
    return this.descending() ? 'descending' : 'ascending';
  }

  protected date(iso: string) {
    return new Intl.DateTimeFormat(this.i18n.locale(), { dateStyle: 'medium' }).format(
      new Date(iso),
    );
  }

  protected shortDate(iso: string) {
    return new Intl.DateTimeFormat(this.i18n.locale(), { day: 'numeric', month: 'short' }).format(
      new Date(iso),
    );
  }

  /** "i går", "for 3 uger siden". */
  protected ago(iso: string | null) {
    if (!iso) return this.i18n.t().planner.admin.never;
    const rtf = new Intl.RelativeTimeFormat(this.i18n.locale(), { numeric: 'auto' });
    const minutes = (new Date(iso).getTime() - Date.now()) / 60_000;
    const units: [Intl.RelativeTimeFormatUnit, number][] = [
      ['year', 525_600],
      ['month', 43_200],
      ['week', 10_080],
      ['day', 1_440],
      ['hour', 60],
    ];
    for (const [unit, size] of units)
      if (Math.abs(minutes) >= size) return rtf.format(Math.round(minutes / size), unit);
    return rtf.format(Math.round(minutes), 'minute');
  }
}
