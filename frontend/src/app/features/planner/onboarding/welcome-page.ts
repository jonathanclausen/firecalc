import { NgTemplateOutlet } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  Account,
  AccountType,
  Dashboard,
  PlannerApi,
  Scenario,
  today,
} from '../../../core/api/planner-api';
import { Auth } from '../../../core/auth/auth';
import { ageReaching, project } from '../../../core/finance/projection';
import { I18n } from '../../../core/i18n/i18n';
import { DecimalInput } from '../../../shared/decimal-input';
import { HelpTip } from '../../../shared/help-tip';
import { MoneyPipe } from '../../../shared/money.pipe';
import { formatDecimal, parseDecimal } from '../../../shared/parse-decimal';
import { startingPoint } from '../future/future-data';

type Step = 'welcome' | 'about' | 'accounts' | 'goal' | 'done';

/** The steps with a number in "Trin 1 af 3"; the welcome and the last screen stand outside. */
const NUMBERED: Step[] = ['about', 'accounts', 'goal'];

/** The account kinds offered, in the order shown. */
const OPTIONS: AccountType[] = ['investment', 'savings', 'cash', 'property', 'loan'];

/** Defaults for a first plan; the same as a new scenario on the Fremtid page. */
const DEFAULTS = {
  investmentReturnPct: 7,
  savingsReturnPct: 1.5,
  homeGrowthPct: 2,
  inflationPct: 2,
  withdrawalPct: 4,
};

interface AccountDraft {
  type: AccountType;
  chosen: boolean;
  name: string;
  value: string;
  /** For a home: what is owed on it. */
  loan: string;
}

interface Summary {
  netWorth: number | null;
  currency: string;
  /** Age and year the money that can be spent reaches the goal; null if never, undefined if unknown. */
  reach?: { age: number; year: number } | null;
}

/**
 * The welcome guide a new user sees on first sign-in: who they are, what they have and what they
 * aim for, saved as they go. Every step can be skipped, and it can be opened again from the overview.
 */
@Component({
  selector: 'app-welcome-page',
  imports: [RouterLink, NgTemplateOutlet, DecimalInput, HelpTip, MoneyPipe],
  templateUrl: './welcome-page.html',
  styleUrl: './welcome-page.scss',
})
export class WelcomePage {
  protected readonly i18n = inject(I18n);
  protected readonly auth = inject(Auth);
  private readonly api = inject(PlannerApi);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  protected readonly step = signal<Step>('welcome');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly numbered = NUMBERED;
  protected readonly stepIndex = computed(() => NUMBERED.indexOf(this.step()));

  protected readonly currency = computed(() => this.auth.user()?.currency ?? 'DKK');
  protected readonly todayIso = today();

  // Om dig
  protected readonly birthDate = signal(this.auth.user()?.birthDate ?? '');
  protected readonly fireAge = signal('');
  protected readonly monthlySavings = signal('');

  // Dine konti
  protected readonly existingAccounts = signal(0);
  protected readonly drafts = signal<AccountDraft[]>(this.freshDrafts());
  protected readonly anyChosen = computed(() => this.drafts().some((d) => d.chosen));

  // Dit mål
  protected readonly spending = signal('');
  protected readonly target = signal('');
  /** The FIRE number follows the spending until it is typed over. */
  private targetTouched = false;

  protected readonly summary = signal<Summary | null>(null);

  constructor() {
    void firstValueFrom(this.http.get<Account[]>('/api/accounts'))
      .then((list) => this.existingAccounts.set(list.length))
      .catch(() => undefined);
  }

  private freshDrafts(): AccountDraft[] {
    const options = this.i18n.t().planner.onboarding.accounts.options;
    return OPTIONS.map((type) => ({
      type,
      chosen: false,
      name: options[type].name,
      value: '',
      loan: '',
    }));
  }

  protected go(step: Step) {
    this.error.set(null);
    this.step.set(step);
    // A new step reads from the top, which matters on a phone.
    if (typeof window !== 'undefined') window.scrollTo({ top: 0 });
  }

  protected back() {
    const i = this.stepIndex();
    this.go(i <= 0 ? 'welcome' : NUMBERED[i - 1]);
  }

  /** Skipping a step moves on without saving it. */
  protected skip() {
    const i = this.stepIndex();
    if (i === NUMBERED.length - 1) void this.finish();
    else this.go(NUMBERED[i + 1]);
  }

  protected updateDraft(type: AccountType, change: Partial<AccountDraft>) {
    this.drafts.update((list) => list.map((d) => (d.type === type ? { ...d, ...change } : d)));
  }

  protected setSpending(text: string) {
    this.spending.set(text);
    if (this.targetTouched) return;
    const value = this.number(text);
    this.target.set(value ? formatDecimal(value * 25, this.i18n.lang()) : '');
  }

  protected setTarget(text: string) {
    this.targetTouched = true;
    this.target.set(text);
  }

  private number(text: string) {
    return parseDecimal(text, this.i18n.lang());
  }

  /** "Spring introduktionen over": never open it by itself again. */
  protected async skipAll() {
    await this.run(async () => {
      this.auth.user.set(await this.api.saveOnboarding({ onboarded: true }));
      await this.router.navigateByUrl('/planner');
    });
  }

  protected async tryDemo() {
    await this.run(async () => {
      await this.api.loadDemo(this.i18n.lang());
      this.auth.user.set(await this.api.saveOnboarding({ onboarded: true }));
      await this.router.navigateByUrl('/planner');
    });
  }

  protected async saveAbout(event: Event) {
    event.preventDefault();
    const birth = this.birthDate();
    await this.run(async () => {
      if (birth && birth !== this.auth.user()?.birthDate)
        this.auth.user.set(await this.api.saveProfile({ birthDate: birth }));
      this.go('accounts');
    });
  }

  protected async saveAccounts(event: Event) {
    event.preventDefault();
    const chosen = this.drafts().filter((d) => d.chosen);
    if (!chosen.length) {
      this.error.set(this.i18n.t().planner.onboarding.accounts.noneChosen);
      return;
    }
    await this.run(async () => {
      for (const d of chosen) {
        const account = await this.api.createAccount({
          name: d.name.trim() || this.i18n.t().planner.onboarding.accounts.options[d.type].name,
          type: d.type,
        });
        // Created, so a retry after an error doesn't add it twice.
        this.updateDraft(d.type, { chosen: false });
        this.existingAccounts.update((n) => n + 1);
        const value = this.number(d.value);
        const loan = d.type === 'property' ? this.number(d.loan) : null;
        if (value !== null && value >= 0)
          await this.api.saveBalance(account.id, {
            date: today(),
            balance: value,
            loan: loan !== null && loan >= 0 ? loan : null,
          });
      }
      this.go('goal');
    });
  }

  protected async saveGoal(event: Event) {
    event.preventDefault();
    const target = this.number(this.target());
    await this.run(async () => {
      if (target !== null && target > 0)
        await this.api.saveGoal({
          name: 'FIRE',
          targetAmount: target,
          targetDate: null,
          expectedAnnualReturnPct: DEFAULTS.investmentReturnPct,
        });
      await this.complete();
    });
  }

  private finish() {
    return this.run(() => this.complete());
  }

  /** Saves a first plan for the Fremtid page, marks the guide done and works out the summary. */
  private async complete() {
    const birth = this.auth.user()?.birthDate ?? null;
    const savings = this.number(this.monthlySavings());
    const fireAge = this.number(this.fireAge());
    const spending = this.number(this.spending());
    const plan = {
      ...DEFAULTS,
      name: this.i18n.t().planner.onboarding.scenarioName,
      monthlySavings: savings !== null && savings > 0 ? savings : 0,
      fireAge: fireAge !== null && fireAge > 0 && fireAge <= 100 ? fireAge : 60,
      yearlySpending: spending !== null && spending > 0 ? spending : 0,
      events: [],
    };

    if (birth && (savings !== null || fireAge !== null)) {
      const scenarios = await firstValueFrom(this.http.get<Scenario[]>('/api/scenarios'));
      if (!scenarios.length) await this.api.createScenario(plan);
    }
    this.auth.user.set(await this.api.saveOnboarding({ onboarded: true }));

    const dashboard = await firstValueFrom(this.http.get<Dashboard>('/api/dashboard'));
    const summary: Summary = {
      netWorth: dashboard.latest?.total ?? null,
      currency: dashboard.currency,
    };
    const goal = dashboard.goal?.goal.targetAmount;
    if (birth && goal) {
      // Keep saving until the goal is reached: no FIRE age in the way.
      const { points } = project(
        startingPoint(dashboard),
        { ...plan, fireAge: 200 },
        birth,
        today(),
      );
      const liquid = points.map((p) => ({ ...p, netWorth: p.liquid }));
      const age = ageReaching(liquid, goal);
      const at = liquid.find((p) => p.age === age);
      summary.reach = age === null || !at ? null : { age, year: Number(at.date.slice(0, 4)) };
    }
    this.summary.set(summary);
    this.go('done');
  }

  private async run(action: () => Promise<void>) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch {
      this.error.set(this.i18n.t().planner.error);
    } finally {
      this.busy.set(false);
    }
  }
}
