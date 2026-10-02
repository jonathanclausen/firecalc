import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Account, PlannerApi, Portfolio, Position } from '../../../core/api/planner-api';
import { I18n } from '../../../core/i18n/i18n';
import { CurrencySettings } from '../../../core/settings/currency';
import { MoneyPipe } from '../../../shared/money.pipe';

/** Holdings per investment account, valued with the latest stored prices (/planner/portfolio). */
@Component({
  selector: 'app-portfolio-page',
  imports: [RouterLink, MoneyPipe],
  templateUrl: './portfolio-page.html',
  styleUrl: './portfolio-page.scss',
})
export class PortfolioPage {
  protected readonly i18n = inject(I18n);
  private readonly currencySettings = inject(CurrencySettings);
  private readonly api = inject(PlannerApi);

  protected readonly portfolio = httpResource<Portfolio>(() => '/api/portfolio');
  protected readonly accounts = httpResource<Account[]>(() => '/api/accounts');

  protected readonly currency = computed(() => this.portfolio.value()?.currency ?? 'DKK');
  protected readonly loading = computed(
    () => !this.portfolio.hasValue() || !this.accounts.hasValue(),
  );
  protected readonly failed = computed(() => !!(this.portfolio.error() || this.accounts.error()));
  protected readonly hasInvestmentAccount = computed(() =>
    (this.accounts.value() ?? []).some((a) => a.type === 'investment'),
  );

  /** Sums across accounts for the hero card. */
  protected readonly totals = computed(() => {
    const list = this.portfolio.value()?.accounts ?? [];
    const sum = (pick: (a: (typeof list)[number]) => number) =>
      list.reduce((s, a) => s + pick(a), 0);
    const dates = list.map((a) => a.pricesAsOf).filter((d): d is string => !!d);
    return {
      gain: sum((a) => a.unrealizedGain),
      growth: sum((a) => a.growth),
      dividends: sum((a) => a.dividends),
      cash: sum((a) => a.cash),
      pricesAsOf: dates.length ? dates.sort().at(-1)! : null,
    };
  });

  /** The position whose ticker is being corrected, with the draft symbol. */
  protected readonly editing = signal<{ instrumentId: string; symbol: string } | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected signed(value: number) {
    const text = this.currencySettings.format(value, { currency: this.currency() });
    return value > 0 ? `+${text}` : text;
  }

  protected signedPct(value: number) {
    const text = this.i18n.percent(value, 1);
    return value > 0 ? `+${text}` : text;
  }

  protected startEdit(p: Position) {
    this.editing.set({ instrumentId: p.instrumentId, symbol: p.symbol ?? '' });
  }

  protected async saveSymbol(event: Event) {
    event.preventDefault();
    const draft = this.editing();
    if (!draft) return;
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.updateInstrument(draft.instrumentId, { symbol: draft.symbol.trim() || null });
      this.editing.set(null);
      this.portfolio.reload();
    } catch {
      this.error.set(this.i18n.t().planner.error);
    } finally {
      this.busy.set(false);
    }
  }
}
