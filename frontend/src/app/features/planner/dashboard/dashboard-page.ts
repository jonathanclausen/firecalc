import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Account, Dashboard, PlannerApi, Snapshot, today } from '../../../core/api/planner-api';
import {
  averageMonthlyChange,
  monthsBetween,
  requiredMonthlySaving,
} from '../../../core/finance/goal';
import { I18n } from '../../../core/i18n/i18n';
import { CurrencySettings } from '../../../core/settings/currency';
import { MoneyPipe } from '../../../shared/money.pipe';
import { NetWorthChart } from './net-worth-chart';

@Component({
  selector: 'app-dashboard-page',
  imports: [RouterLink, MoneyPipe, NetWorthChart],
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.scss',
})
export class DashboardPage {
  protected readonly i18n = inject(I18n);
  private readonly currencySettings = inject(CurrencySettings);
  private readonly api = inject(PlannerApi);

  protected readonly dashboard = httpResource<Dashboard>(() => '/api/dashboard');
  protected readonly accounts = httpResource<Account[]>(() => '/api/accounts');
  protected readonly snapshots = httpResource<Snapshot[]>(() => '/api/snapshots');

  protected readonly loading = computed(
    () => !this.dashboard.hasValue() || !this.accounts.hasValue() || !this.snapshots.hasValue(),
  );
  protected readonly failed = computed(
    () => !!(this.dashboard.error() || this.accounts.error() || this.snapshots.error()),
  );
  protected readonly deleteError = signal(false);

  /** Balances are stored in the user's own currency, whatever the header selector says. */
  protected readonly currency = computed(() => this.dashboard.value()?.currency ?? 'DKK');

  protected readonly typeTotals = computed(() => {
    const latest = this.dashboard.value()?.latest;
    if (!latest) return [];
    return (['investment', 'savings', 'cash'] as const)
      .filter((type) => latest.byType[type] !== undefined)
      .map((type) => ({ type, amount: latest.byType[type]! }));
  });

  protected readonly pace = computed(() => {
    const series = this.dashboard.value()?.series ?? [];
    const perMonth = averageMonthlyChange(series);
    return perMonth === null ? null : { perMonth, since: series[0].date };
  });

  protected readonly goal = computed(() => {
    const progress = this.dashboard.value()?.goal;
    if (!progress) return null;
    const { goal, current } = progress;
    const rate = goal.expectedAnnualReturnPct ?? 0;
    const months = goal.targetDate ? monthsBetween(today(), goal.targetDate) : null;
    const needed =
      months === null ? undefined : requiredMonthlySaving(current, goal.targetAmount, months, rate);
    return { ...progress, rate, needed, barPct: Math.min(100, Math.max(0, progress.progressPct)) };
  });

  /** Newest first, each with the change from the snapshot before it. */
  protected readonly history = computed(() => {
    const list = this.snapshots.value() ?? [];
    return list.map((s, i) => ({
      ...s,
      change: i + 1 < list.length ? s.total - list[i + 1].total : null,
    }));
  });

  protected signed(value: number) {
    const text = this.currencySettings.format(value, { currency: this.currency() });
    return value > 0 ? `+${text}` : text;
  }

  protected async remove(snapshot: Snapshot) {
    const t = this.i18n.t().planner.dashboard;
    if (!confirm(t.confirmDelete(this.i18n.date(snapshot.date)))) return;
    this.deleteError.set(false);
    try {
      await this.api.deleteSnapshot(snapshot.id);
      this.snapshots.reload();
      this.dashboard.reload();
    } catch {
      this.deleteError.set(true);
    }
  }
}
