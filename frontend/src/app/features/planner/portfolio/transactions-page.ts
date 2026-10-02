import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  Account,
  OUTFLOW_TYPES,
  PlannerApi,
  SHARE_TYPES,
  SaveTransaction,
  TRANSACTION_TYPES,
  Transaction,
  TransactionType,
  today,
} from '../../../core/api/planner-api';
import { Auth } from '../../../core/auth/auth';
import { I18n } from '../../../core/i18n/i18n';
import { CurrencySettings } from '../../../core/settings/currency';
import { InstrumentChoice, InstrumentPicker } from './instrument-picker';

/** One investment account's transactions, with a form to add or edit one (/planner/portfolio/:accountId). */
@Component({
  selector: 'app-transactions-page',
  imports: [RouterLink, InstrumentPicker],
  templateUrl: './transactions-page.html',
  styleUrl: './transactions-page.scss',
})
export class TransactionsPage {
  /** Route parameter. */
  readonly accountId = input.required<string>();

  protected readonly i18n = inject(I18n);
  private readonly currencySettings = inject(CurrencySettings);
  private readonly api = inject(PlannerApi);
  private readonly auth = inject(Auth);

  protected readonly currency = computed(() => this.auth.user()?.currency ?? 'DKK');
  protected readonly types = TRANSACTION_TYPES;
  protected readonly accounts = httpResource<Account[]>(() => '/api/accounts?includeArchived=true');
  protected readonly transactions = httpResource<Transaction[]>(
    () => `/api/accounts/${this.accountId()}/transactions`,
  );
  protected readonly account = computed(
    () => this.accounts.value()?.find((a) => a.id === this.accountId()) ?? null,
  );

  /** Shares and funds already used in this account, for quick picking. */
  protected readonly known = computed(() => {
    const seen = new Map<string, InstrumentChoice>();
    for (const t of this.transactions.value() ?? []) {
      if (t.instrumentId && !seen.has(t.instrumentId)) {
        seen.set(t.instrumentId, {
          id: t.instrumentId,
          name: t.instrumentName ?? '',
          label: [t.instrumentName, t.symbol ?? t.isin].filter(Boolean).join(' · '),
        });
      }
    }
    return [...seen.values()].sort((a, b) => a.name.localeCompare(b.name));
  });

  // Form state. editingId is null for a new transaction.
  protected readonly editingId = signal<string | null>(null);
  protected readonly date = signal(today());
  protected readonly type = signal<TransactionType>('buy');
  protected readonly instrument = signal<InstrumentChoice | null>(null);
  protected readonly quantity = signal('');
  protected readonly amount = signal('');
  protected readonly price = signal('');
  protected readonly note = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly needsShares = computed(() => SHARE_TYPES.has(this.type()));
  /** Dividends and interest can also be tied to a share, but don't have to be. */
  protected readonly allowsShares = computed(
    () => this.needsShares() || this.type() === 'dividend' || this.type() === 'tax',
  );

  protected money(value: number) {
    return this.currencySettings.format(value, { currency: this.currency() });
  }

  protected edit(t: Transaction) {
    this.editingId.set(t.id);
    this.date.set(t.date);
    this.type.set(t.type);
    this.instrument.set(
      t.instrumentId
        ? {
            id: t.instrumentId,
            name: t.instrumentName ?? '',
            label: [t.instrumentName, t.symbol ?? t.isin].filter(Boolean).join(' · '),
          }
        : null,
    );
    this.quantity.set(t.quantity ? String(t.quantity) : '');
    this.amount.set(String(OUTFLOW_TYPES.has(t.type) ? -t.amount : t.amount));
    this.price.set(t.price === null ? '' : String(t.price));
    this.note.set(t.note ?? '');
    this.error.set(null);
  }

  protected reset() {
    this.editingId.set(null);
    this.instrument.set(null);
    this.quantity.set('');
    this.amount.set('');
    this.price.set('');
    this.note.set('');
    this.error.set(null);
  }

  protected async save(event: Event) {
    event.preventDefault();
    const t = this.i18n.t().planner;
    const type = this.type();
    const amount = parse(this.amount());
    if (amount === null) return;

    const instrument = this.allowsShares() ? this.instrument() : null;
    const body: SaveTransaction = {
      date: this.date(),
      type,
      instrument: instrument
        ? {
            id: instrument.id,
            isin: instrument.isin,
            symbol: instrument.symbol,
            name: instrument.name,
          }
        : null,
      quantity: this.allowsShares() ? parse(this.quantity()) : null,
      price: parse(this.price()),
      // People type the amount as a positive number; outflows are stored negative.
      amount: OUTFLOW_TYPES.has(type)
        ? -Math.abs(amount)
        : type === 'other'
          ? amount
          : Math.abs(amount),
      note: this.note().trim() || null,
    };

    this.busy.set(true);
    this.error.set(null);
    try {
      const id = this.editingId();
      await (id
        ? this.api.updateTransaction(id, body)
        : this.api.createTransaction(this.accountId(), body));
      this.reset();
      this.transactions.reload();
    } catch {
      this.error.set(t.error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(t: Transaction) {
    if (!confirm(this.i18n.t().planner.transactionsPage.confirmDelete)) return;
    this.busy.set(true);
    try {
      await this.api.deleteTransaction(t.id);
      if (this.editingId() === t.id) this.reset();
      this.transactions.reload();
    } catch {
      this.error.set(this.i18n.t().planner.error);
    } finally {
      this.busy.set(false);
    }
  }
}

function parse(raw: string): number | null {
  if (raw.trim() === '') return null;
  const value = Number(raw.replace(',', '.'));
  return Number.isFinite(value) ? value : null;
}
