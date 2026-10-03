import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  Account,
  AccountPortfolio,
  InstrumentRef,
  PlannerApi,
  Portfolio,
  Position,
} from '../../../core/api/planner-api';
import { I18n } from '../../../core/i18n/i18n';
import type { Lang } from '../../../core/i18n/translations';
import { CurrencySettings } from '../../../core/settings/currency';
import { MoneyPipe } from '../../../shared/money.pipe';
import { InstrumentChoice, InstrumentPicker } from './instrument-picker';
import { PortfolioHistoryCard } from './history/portfolio-history';
import { DecimalInput } from '../../../shared/decimal-input';
import { formatDecimal, parseDecimal } from '../../../shared/parse-decimal';

/** Holdings per investment account, valued with the latest stored prices (/planner/portfolio). */
@Component({
  selector: 'app-portfolio-page',
  imports: [RouterLink, MoneyPipe, InstrumentPicker, DecimalInput, PortfolioHistoryCard],
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

  /** Every active investment account, with its valuation once it has holdings. */
  protected readonly cards = computed(() => {
    const valued = new Map((this.portfolio.value()?.accounts ?? []).map((a) => [a.accountId, a]));
    return (this.accounts.value() ?? [])
      .filter((a) => a.type === 'investment')
      .map((account) => ({ account, data: valued.get(account.id) ?? null }));
  });

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
  /** Latest allowed purchase date. */
  protected readonly today = new Date().toISOString().slice(0, 10);
  protected readonly error = signal<string | null>(null);

  /** The account a holding is being added to, with the draft. */
  protected readonly adding = signal<{
    accountId: string;
    instrument: InstrumentChoice | null;
    quantity: string;
    price: string;
    date: string;
  } | null>(null);
  /** Latest price of a newly searched listing, so it can be checked against the broker. */
  protected readonly quote = httpResource<{ currency: string; price: number; date: string }>(() => {
    const chosen = this.adding()?.instrument;
    return chosen?.symbol && !chosen.id
      ? `/api/instruments/quote?symbol=${encodeURIComponent(chosen.symbol)}`
      : undefined;
  });
  /** The holding being edited: its GAK, or a purchase or sale of more shares. */
  protected readonly counting = signal<{
    accountId: string;
    instrumentId: string;
    currency: string | null;
    mode: 'gak' | 'buy' | 'sell';
    /** The count and GAK when the form opened, so only what was changed is sent. */
    quantityWas: number;
    averageWas: string;
    average: string;
    tradeQuantity: string;
    tradePrice: string;
    tradeDate: string;
  } | null>(null);

  // Patches read the latest draft, so quick successive inputs never overwrite each other.
  protected patchAdding(patch: Partial<NonNullable<ReturnType<typeof this.adding>>>) {
    this.adding.update((d) => (d ? { ...d, ...patch } : d));
  }

  protected patchCounting(patch: Partial<NonNullable<ReturnType<typeof this.counting>>>) {
    this.counting.update((d) => (d ? { ...d, ...patch } : d));
  }

  /** Currency of the instrument being added, so the price field says what it expects. */
  protected readonly addingCurrency = computed(() => {
    const chosen = this.adding()?.instrument;
    if (!chosen) return '';
    if (chosen.id) {
      const pos = this.portfolio
        .value()
        ?.accounts.flatMap((a) => a.positions)
        .find((p) => p.instrumentId === chosen.id);
      return pos?.currency ?? '';
    }
    return this.quote.value()?.currency ?? '';
  });

  protected known(data: AccountPortfolio | null): InstrumentChoice[] {
    return (data?.positions ?? []).map((p) => ({
      id: p.instrumentId,
      name: p.name,
      label: [p.name, p.symbol ?? p.isin].filter(Boolean).join(' · '),
    }));
  }

  protected startAdd(accountId: string) {
    this.counting.set(null);
    this.adding.set({ accountId, instrument: null, quantity: '', price: '', date: '' });
  }

  protected startCount(accountId: string, p: Position) {
    this.adding.set(null);
    const average = currentAverage(p, this.i18n.lang());
    this.counting.set({
      accountId,
      instrumentId: p.instrumentId,
      currency: p.currency,
      mode: 'gak',
      quantityWas: p.quantity,
      averageWas: average,
      average,
      tradeQuantity: '',
      tradePrice: '',
      tradeDate: '',
    });
  }

  protected async saveAdd(event: Event) {
    event.preventDefault();
    const draft = this.adding();
    const quantity = parse(draft?.quantity, this.i18n.lang());
    if (!draft?.instrument || quantity === null || parse(draft.price, this.i18n.lang()) === null)
      return;
    const { id, isin, symbol, name } = draft.instrument;
    await this.saveHolding(
      draft.accountId,
      { id, isin, symbol, name },
      quantity,
      draft.price,
      draft.date || null,
    );
  }

  protected async saveCount(event: Event) {
    event.preventDefault();
    const draft = this.counting();
    if (!draft) return;
    const holding = { id: draft.instrumentId };

    if (draft.mode === 'gak') {
      const average =
        draft.average !== draft.averageWas ? parse(draft.average, this.i18n.lang()) : null;
      if (average === null) {
        this.counting.set(null);
        return;
      }
      await this.saveHolding(draft.accountId, holding, draft.quantityWas, '', null, {
        averagePrice: average,
      });
      return;
    }

    // A trade is booked as the new count, at the price and date given (today's when left empty).
    const traded = parse(draft.tradeQuantity, this.i18n.lang());
    if (!traded) return;
    const quantity = draft.quantityWas + (draft.mode === 'buy' ? traded : -traded);
    if (quantity < 0) {
      this.error.set(this.i18n.t().planner.portfolioPage.sellTooMany);
      return;
    }
    await this.saveHolding(
      draft.accountId,
      holding,
      quantity,
      draft.tradePrice,
      draft.tradeDate || null,
    );
  }

  private async saveHolding(
    accountId: string,
    instrument: InstrumentRef,
    quantity: number,
    price: string,
    date: string | null = null,
    extra: { averagePrice?: number | null } = {},
  ) {
    const t = this.i18n.t().planner;
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.setHolding(accountId, {
        instrument,
        quantity,
        unitPrice: parse(price, this.i18n.lang()),
        date,
        ...extra,
      });
      this.adding.set(null);
      this.counting.set(null);
      this.portfolio.reload();
    } catch (e) {
      const errors = e instanceof HttpErrorResponse && e.status === 400 ? e.error?.errors : null;
      this.error.set(
        errors?.unitPrice
          ? t.portfolioPage.needPrice
          : errors?.averagePrice
            ? t.portfolioPage.cannotCorrectAverage
            : t.error,
      );
    } finally {
      this.busy.set(false);
    }
  }

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

function parse(raw: string | undefined, lang: Lang): number | null {
  if (raw === undefined || raw.trim() === '') return null;
  const value = parseDecimal(raw, lang);
  return value !== null && value >= 0 ? value : null;
}

/**
 * The average price paid per share in the share's own currency, for showing in the form. Foreign
 * shares are converted back at today's rate, so it can differ slightly from the broker's GAK.
 */
function currentAverage(p: Position, lang: Lang): string {
  if (p.quantity <= 0 || p.costBasis <= 0) return '';
  const perShare =
    p.price && p.value > 0 && !p.priceMissing
      ? (p.costBasis * p.price) / p.value
      : p.costBasis / p.quantity;
  return formatDecimal(Math.round(perShare * 100) / 100, lang);
}
