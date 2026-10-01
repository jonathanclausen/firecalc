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
}

/** Today's date as yyyy-mm-dd in the visitor's time zone. */
export function today(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}
