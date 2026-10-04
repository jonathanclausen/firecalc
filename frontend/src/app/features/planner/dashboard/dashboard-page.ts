import { httpResource } from '@angular/common/http';
import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ACCOUNT_TYPES, Account, Dashboard, today } from '../../../core/api/planner-api';
import {
  averageMonthlyChange,
  monthsBetween,
  requiredMonthlySaving,
} from '../../../core/finance/goal';
import { I18n } from '../../../core/i18n/i18n';
import { readStorage, writeStorage } from '../../../core/storage';
import { CurrencySettings } from '../../../core/settings/currency';
import { MoneyPipe } from '../../../shared/money.pipe';
import { NetWorthChart } from './net-worth-chart';

export const INCLUDE_HOME_KEY = 'firecalc.includeHome';

@Component({
  selector: 'app-dashboard-page',
  imports: [RouterLink, MoneyPipe, NetWorthChart],
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.scss',
})
export class DashboardPage {
  protected readonly i18n = inject(I18n);
  private readonly currencySettings = inject(CurrencySettings);

  /** Whether home equity counts in net worth, the chart and the goal. Remembered on this device. */
  protected readonly includeHome = signal(readStorage(INCLUDE_HOME_KEY) !== 'false');

  private readonly dashboard = httpResource<Dashboard>(() =>
    this.includeHome() ? '/api/dashboard' : '/api/dashboard?includeHome=false',
  );
  /** The dashboard, keeping the previous one on screen while the switch reloads it. */
  protected readonly view = linkedSignal<Dashboard | undefined, Dashboard | undefined>({
    source: () => this.dashboard.value(),
    computation: (value, previous) => value ?? previous?.value,
  });
  protected readonly accounts = httpResource<Account[]>(() => '/api/accounts');

  /** The switch only shows once there is a home, or a loan for one, to leave out. */
  protected readonly hasHome = computed(() =>
    (this.accounts.value() ?? []).some(
      (a) => (a.type === 'property' || a.partOfHome) && !a.archived,
    ),
  );

  protected readonly loading = computed(() => !this.view() || !this.accounts.hasValue());
  protected readonly failed = computed(() => !!(this.dashboard.error() || this.accounts.error()));

  /** Balances are stored in the user's own currency, whatever the header selector says. */
  protected readonly currency = computed(() => this.view()?.currency ?? 'DKK');

  protected setIncludeHome(on: boolean) {
    this.includeHome.set(on);
    writeStorage(INCLUDE_HOME_KEY, String(on));
  }

  protected readonly typeTotals = computed(() => {
    const latest = this.view()?.latest;
    if (!latest) return [];
    return ACCOUNT_TYPES.filter((type) => latest.byType[type] !== undefined).map((type) => ({
      type,
      amount: latest.byType[type]!,
    }));
  });

  protected readonly pace = computed(() => {
    const series = this.view()?.series ?? [];
    const perMonth = averageMonthlyChange(series);
    return perMonth === null ? null : { perMonth, since: series[0].date };
  });

  protected readonly goal = computed(() => {
    const progress = this.view()?.goal;
    if (!progress) return null;
    const { goal, current } = progress;
    const rate = goal.expectedAnnualReturnPct ?? 0;
    const months = goal.targetDate ? monthsBetween(today(), goal.targetDate) : null;
    const needed =
      months === null ? undefined : requiredMonthlySaving(current, goal.targetAmount, months, rate);
    return { ...progress, rate, needed, barPct: Math.min(100, Math.max(0, progress.progressPct)) };
  });

  protected signed(value: number) {
    const text = this.currencySettings.format(value, { currency: this.currency() });
    return value > 0 ? `+${text}` : text;
  }
}
