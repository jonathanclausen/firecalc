import { httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Goal, PlannerApi } from '../../../core/api/planner-api';
import { Auth } from '../../../core/auth/auth';
import { I18n } from '../../../core/i18n/i18n';
import { DecimalInput } from '../../../shared/decimal-input';
import { formatDecimal, parseDecimal } from '../../../shared/parse-decimal';

@Component({
  selector: 'app-goal-page',
  imports: [RouterLink, DecimalInput],
  templateUrl: './goal-page.html',
})
export class GoalPage {
  protected readonly i18n = inject(I18n);
  private readonly api = inject(PlannerApi);
  private readonly router = inject(Router);
  private readonly auth = inject(Auth);
  protected readonly currency = computed(() => this.auth.user()?.currency ?? 'DKK');

  /** GET /api/goal answers 204 when no goal is set, which arrives as a null body. */
  protected readonly goal = httpResource<Goal | null>(() => '/api/goal');

  protected readonly targetAmount = signal('');
  protected readonly targetDate = signal('');
  protected readonly expectedReturn = signal('7');
  protected readonly placeholder = computed(() => formatDecimal(5_000_000, this.i18n.lang()));
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    let filled = false;
    effect(() => {
      if (filled || !this.goal.hasValue()) return;
      filled = true;
      const g = this.goal.value();
      if (!g) return;
      const lang = this.i18n.lang();
      this.targetAmount.set(formatDecimal(g.targetAmount, lang));
      this.targetDate.set(g.targetDate ?? '');
      this.expectedReturn.set(formatDecimal(g.expectedAnnualReturnPct, lang));
    });
  }

  protected async save(event: Event) {
    event.preventDefault();
    const t = this.i18n.t().planner;
    const amount = parseDecimal(this.targetAmount(), this.i18n.lang());
    if (amount === null || amount <= 0) {
      this.error.set(t.goalPage.invalidAmount);
      return;
    }
    const rate = parseDecimal(this.expectedReturn(), this.i18n.lang());
    await this.run(() =>
      this.api.saveGoal({
        name: this.goal.value()?.name ?? 'FIRE',
        targetAmount: amount,
        targetDate: this.targetDate() || null,
        expectedAnnualReturnPct: rate,
      }),
    );
  }

  protected remove() {
    return this.run(() => this.api.deleteGoal());
  }

  private async run(action: () => Promise<unknown>) {
    this.saving.set(true);
    this.error.set(null);
    try {
      await action();
      await this.router.navigateByUrl('/planner');
    } catch {
      this.error.set(this.i18n.t().planner.error);
    } finally {
      this.saving.set(false);
    }
  }
}
