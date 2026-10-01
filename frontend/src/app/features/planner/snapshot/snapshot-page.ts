import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Account, PlannerApi, Snapshot, today } from '../../../core/api/planner-api';
import { Auth } from '../../../core/auth/auth';
import { I18n } from '../../../core/i18n/i18n';
import { MoneyPipe } from '../../../shared/money.pipe';

/** Records a new snapshot (route /planner/snapshot) or edits one (/planner/snapshot/:id). */
@Component({
  selector: 'app-snapshot-page',
  imports: [RouterLink, MoneyPipe],
  templateUrl: './snapshot-page.html',
  styleUrl: './snapshot-page.scss',
})
export class SnapshotPage {
  /** Route parameter; absent for a new snapshot. */
  readonly id = input<string>();

  protected readonly i18n = inject(I18n);
  private readonly auth = inject(Auth);
  protected readonly currency = computed(() => this.auth.user()?.currency ?? 'DKK');
  private readonly api = inject(PlannerApi);
  private readonly router = inject(Router);

  protected readonly accounts = httpResource<Account[]>(() => '/api/accounts?includeArchived=true');
  protected readonly snapshots = httpResource<Snapshot[]>(() => '/api/snapshots');

  protected readonly date = signal(today());
  protected readonly note = signal('');
  /** Raw text per account id, so an empty field can mean "leave this account out". */
  protected readonly balances = signal<Record<string, string>>({});
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly editing = computed(() => {
    const id = this.id();
    return id ? (this.snapshots.value()?.find((s) => s.id === id) ?? null) : null;
  });

  protected readonly ready = computed(() => this.accounts.hasValue() && this.snapshots.hasValue());
  protected readonly notFound = computed(() => this.ready() && !!this.id() && !this.editing());

  /** Active accounts, plus archived ones that already have a balance in the edited snapshot. */
  protected readonly rows = computed(() => {
    const inSnapshot = new Set(this.editing()?.entries.map((e) => e.accountId));
    return (this.accounts.value() ?? []).filter((a) => !a.archived || inSnapshot.has(a.id));
  });

  protected readonly total = computed(() => {
    const balances = this.balances();
    return this.rows().reduce((sum, a) => sum + (parse(balances[a.id]) ?? 0), 0);
  });

  constructor() {
    // Fill the form once the data is in: the edited snapshot, or the latest one as a starting point.
    let filled = false;
    effect(() => {
      if (filled || !this.ready()) return;
      filled = true;
      const source = this.id() ? this.editing() : (this.snapshots.value()?.[0] ?? null);
      const values: Record<string, string> = {};
      for (const e of source?.entries ?? []) values[e.accountId] = String(e.balance);
      this.balances.set(values);
      if (this.id() && source) {
        this.date.set(source.date);
        this.note.set(source.note ?? '');
      }
    });
  }

  protected setBalance(accountId: string, raw: string) {
    this.balances.update((b) => ({ ...b, [accountId]: raw }));
  }

  protected async save(event: Event) {
    event.preventDefault();
    const t = this.i18n.t().planner;
    const visible = new Set(this.rows().map((a) => a.id));
    const entries = Object.entries(this.balances())
      .filter(([id]) => visible.has(id))
      .map(([accountId, raw]) => ({ accountId, balance: parse(raw) }))
      .filter((e): e is { accountId: string; balance: number } => e.balance !== null);
    if (entries.length === 0) {
      this.error.set(t.snapshot.noEntries);
      return;
    }

    const body = { date: this.date(), note: this.note().trim() || null, entries };
    this.saving.set(true);
    this.error.set(null);
    try {
      const id = this.id();
      await (id ? this.api.updateSnapshot(id, body) : this.api.createSnapshot(body));
      await this.router.navigateByUrl('/planner');
    } catch (e) {
      const taken = e instanceof HttpErrorResponse && e.status === 409;
      this.error.set(taken ? t.snapshot.dateTaken : t.error);
    } finally {
      this.saving.set(false);
    }
  }
}

/** Parses a balance field; empty or invalid text means "no balance". */
function parse(raw: string | undefined): number | null {
  if (raw === undefined || raw.trim() === '') return null;
  const value = Number(raw.replace(',', '.'));
  return Number.isFinite(value) && value >= 0 ? value : null;
}
