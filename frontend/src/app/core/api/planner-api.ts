import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Shapes returned by the .NET API under /api. Dates are ISO yyyy-mm-dd strings. */

export type AccountType = 'investment' | 'savings' | 'cash';

export const ACCOUNT_TYPES: readonly AccountType[] = ['investment', 'savings', 'cash'];

export interface Me {
  email: string;
  name: string | null;
  currency: string;
}

export interface Account {
  id: string;
  name: string;
  type: AccountType;
  archived: boolean;
}

export interface SnapshotEntry {
  accountId: string;
  accountName: string;
  accountType: AccountType;
  balance: number;
}

export interface Snapshot {
  id: string;
  date: string;
  note: string | null;
  total: number;
  entries: SnapshotEntry[];
}

export interface SaveSnapshot {
  date: string;
  note: string | null;
  entries: { accountId: string; balance: number }[];
}

export interface Goal {
  name: string;
  targetAmount: number;
  targetDate: string | null;
  expectedAnnualReturnPct: number | null;
}

export interface SeriesPoint {
  date: string;
  total: number;
  byType: Partial<Record<AccountType, number>>;
}

export interface Dashboard {
  currency: string;
  latest: SeriesPoint | null;
  changeSincePrevious: number | null;
  series: SeriesPoint[];
  goal: { goal: Goal; current: number; remaining: number; progressPct: number } | null;
}

export type TransactionType =
  | 'buy'
  | 'sell'
  | 'dividend'
  | 'deposit'
  | 'withdrawal'
  | 'fee'
  | 'tax'
  | 'interest'
  | 'securityIn'
  | 'securityOut'
  | 'other'
  /** A corrected GAK; booked from the holding, not the transaction form. */
  | 'costCorrection';

export const TRANSACTION_TYPES: readonly TransactionType[] = [
  'buy',
  'sell',
  'dividend',
  'deposit',
  'withdrawal',
  'fee',
  'tax',
  'interest',
  'securityIn',
  'securityOut',
  'other',
];

/** Types that move shares and so need a share or fund and a quantity. */
export const SHARE_TYPES: ReadonlySet<TransactionType> = new Set([
  'buy',
  'sell',
  'securityIn',
  'securityOut',
]);

/** Types that take money out of the account; their amount is stored as a negative number. */
export const OUTFLOW_TYPES: ReadonlySet<TransactionType> = new Set([
  'buy',
  'withdrawal',
  'fee',
  'tax',
]);

export interface Position {
  instrumentId: string;
  name: string;
  isin: string | null;
  symbol: string | null;
  currency: string | null;
  quantity: number;
  price: number | null;
  priceDate: string | null;
  value: number;
  costBasis: number;
  gain: number;
  gainPct: number | null;
  dayChange: number | null;
  dividends: number;
  realizedGain: number;
  weightPct: number;
  priceMissing: boolean;
}

export interface AccountPortfolio {
  accountId: string;
  accountName: string;
  archived: boolean;
  value: number;
  marketValue: number;
  cash: number;
  costBasis: number;
  unrealizedGain: number;
  realizedGain: number;
  dividends: number;
  netDeposits: number;
  growth: number;
  dayChange: number;
  pricesAsOf: string | null;
  transactionCount: number;
  positions: Position[];
}

export interface Portfolio {
  currency: string;
  asOf: string;
  value: number;
  dayChange: number;
  accounts: AccountPortfolio[];
}

export interface HistoryPoint {
  date: string;
  value: number;
  netDeposits: number;
  /** Time-weighted return in percent since the start of the period. */
  returnPct: number;
}

export interface PortfolioHistory {
  currency: string;
  firstDate: string | null;
  from: string;
  to: string;
  points: HistoryPoint[];
}

export interface Transaction {
  id: string;
  accountId: string;
  date: string;
  type: TransactionType;
  instrumentId: string | null;
  instrumentName: string | null;
  isin: string | null;
  symbol: string | null;
  quantity: number;
  price: number | null;
  amount: number;
  note: string | null;
  source: string;
}

export interface InstrumentRef {
  id?: string | null;
  isin?: string | null;
  symbol?: string | null;
  name?: string | null;
}

export interface SaveTransaction {
  date: string;
  type: TransactionType;
  instrument: InstrumentRef | null;
  quantity: number | null;
  price: number | null;
  amount: number;
  note: string | null;
}

export interface SymbolMatch {
  symbol: string;
  name: string;
  exchange: string | null;
  type: string | null;
}

export interface ImportResult {
  committed: boolean;
  rows: number;
  new: number;
  duplicates: number;
  skipped: { line: number; reason: string }[];
  otherTypes: string[];
  preview: {
    line: number;
    date: string;
    type: TransactionType;
    rawType: string;
    name: string | null;
    quantity: number;
    amount: number;
  }[];
}

/** Writes to the planner API. Reads use httpResource in the pages so they stay signal-based. */
@Injectable({ providedIn: 'root' })
export class PlannerApi {
  private readonly http = inject(HttpClient);

  createAccount(body: { name: string; type: AccountType }) {
    return firstValueFrom(this.http.post<Account>('/api/accounts', body));
  }

  updateAccount(id: string, body: { name: string; type: AccountType; archived: boolean }) {
    return firstValueFrom(this.http.put<Account>(`/api/accounts/${id}`, body));
  }

  deleteAccount(id: string) {
    return firstValueFrom(this.http.delete<void>(`/api/accounts/${id}`));
  }

  createSnapshot(body: SaveSnapshot) {
    return firstValueFrom(this.http.post<Snapshot>('/api/snapshots', body));
  }

  updateSnapshot(id: string, body: SaveSnapshot) {
    return firstValueFrom(this.http.put<Snapshot>(`/api/snapshots/${id}`, body));
  }

  deleteSnapshot(id: string) {
    return firstValueFrom(this.http.delete<void>(`/api/snapshots/${id}`));
  }

  saveGoal(body: Goal) {
    return firstValueFrom(this.http.put<Goal>('/api/goal', body));
  }

  deleteGoal() {
    return firstValueFrom(this.http.delete<void>('/api/goal'));
  }

  createTransaction(accountId: string, body: SaveTransaction) {
    return firstValueFrom(
      this.http.post<Transaction>(`/api/accounts/${accountId}/transactions`, body),
    );
  }

  updateTransaction(id: string, body: SaveTransaction) {
    return firstValueFrom(this.http.put<Transaction>(`/api/transactions/${id}`, body));
  }

  deleteTransaction(id: string) {
    return firstValueFrom(this.http.delete<void>(`/api/transactions/${id}`));
  }

  /** Sets how many shares the account now holds; the API books the difference as a buy or sale. */
  setHolding(
    accountId: string,
    body: {
      instrument: InstrumentRef;
      quantity: number;
      unitPrice: number | null;
      date?: string | null;
      averagePrice?: number | null;
    },
  ) {
    return firstValueFrom(
      this.http.put<{ quantity: number; change: number; amount: number }>(
        `/api/accounts/${accountId}/holdings`,
        body,
      ),
    );
  }

  /** Sends a Nordnet export as-is. Without commit the server only answers what it would import. */
  /** Imports a broker export; Saxo's is an .xlsx (a zip, starting "PK"), Nordnet's a CSV. */
  importTransactions(accountId: string, file: ArrayBuffer, commit: boolean) {
    const head = new Uint8Array(file.slice(0, 2));
    const broker = head[0] === 0x50 && head[1] === 0x4b ? 'saxo' : 'nordnet';
    return firstValueFrom(
      this.http.post<ImportResult>(`/api/accounts/${accountId}/import/${broker}`, file, {
        params: { commit },
        headers: { 'Content-Type': 'application/octet-stream' },
      }),
    );
  }

  searchInstruments(q: string) {
    return firstValueFrom(
      this.http.get<SymbolMatch[]>('/api/instruments/search', { params: { q } }),
    );
  }

  updateInstrument(id: string, body: { symbol: string | null }) {
    return firstValueFrom(this.http.put<unknown>(`/api/instruments/${id}`, body));
  }
}

/** Today's date as yyyy-mm-dd in the visitor's time zone. */
export function today(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}
