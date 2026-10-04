import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  ACCOUNT_KINDS,
  Account,
  AccountBalance,
  AccountType,
  Me,
  PlannerApi,
  today,
} from '../../../core/api/planner-api';
import { I18n } from '../../../core/i18n/i18n';
import { DecimalInput } from '../../../shared/decimal-input';
import { HelpTip } from '../../../shared/help-tip';
import { MoneyPipe } from '../../../shared/money.pipe';
import { formatDecimal, parseDecimal } from '../../../shared/parse-decimal';

@Component({
  selector: 'app-accounts-page',
  imports: [RouterLink, MoneyPipe, DecimalInput, HelpTip],
  templateUrl: './accounts-page.html',
  styleUrl: './accounts-page.scss',
})
export class AccountsPage {
  protected readonly i18n = inject(I18n);
  private readonly api = inject(PlannerApi);

  protected readonly types = ACCOUNT_KINDS;
  protected readonly accounts = httpResource<Account[]>(() => '/api/accounts?includeArchived=true');
  protected readonly showArchived = signal(false);
  protected readonly visible = computed(() =>
    (this.accounts.value() ?? []).filter((a) => this.showArchived() || !a.archived),
  );
  protected readonly hasArchived = computed(() =>
    (this.accounts.value() ?? []).some((a) => a.archived),
  );

  protected readonly newName = signal('');
  protected readonly newType = signal<AccountType>('investment');
  /** The account being renamed, with its draft values. */
  protected readonly editing = signal<{
    id: string;
    name: string;
    type: AccountType;
  } | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly today = today();
  private readonly me = httpResource<Me>(() => '/api/me');
  /** Balances are stored in the user's own currency, whatever the header selector says. */
  protected readonly currency = computed(() => this.me.value()?.currency ?? 'DKK');

  /** The account whose balance is being entered, with the typed text. */
  protected readonly balanceDraft = signal<{
    id: string;
    balance: string;
    date: string;
  } | null>(null);
  /** The account whose balance history is open. */
  protected readonly historyFor = signal<string | null>(null);
  protected readonly history = httpResource<AccountBalance[]>(() => {
    const id = this.historyFor();
    return id ? `/api/accounts/${id}/balances` : undefined;
  });

  protected startBalance(a: Account) {
    this.editing.set(null);
    const lang = this.i18n.lang();
    this.balanceDraft.set({
      id: a.id,
      balance: a.balance === null ? '' : formatDecimal(a.balance, lang),
      date: this.today,
    });
  }

  /** Changes one field of the open balance form. */
  protected setDraft(field: 'balance' | 'date', value: string) {
    const draft = this.balanceDraft();
    if (draft) this.balanceDraft.set({ ...draft, [field]: value });
  }

  protected async saveBalance(a: Account, event: Event) {
    event.preventDefault();
    const draft = this.balanceDraft();
    const balance = parseDecimal(draft?.balance, this.i18n.lang());
    if (!draft || balance === null || balance < 0 || !draft.date) return;
    await this.run(async () => {
      await this.api.saveBalance(a.id, { date: draft.date, balance });
      this.balanceDraft.set(null);
      if (this.historyFor() === a.id) this.history.reload();
    });
  }

  protected toggleHistory(a: Account) {
    this.historyFor.set(this.historyFor() === a.id ? null : a.id);
  }

  protected async removeBalance(a: Account, b: AccountBalance) {
    const t = this.i18n.t().planner.accountsPage;
    if (!confirm(t.confirmDeleteBalance(this.i18n.date(b.date)))) return;
    await this.run(async () => {
      await this.api.deleteBalance(a.id, b.date);
      this.history.reload();
    });
  }

  protected async add(event: Event) {
    event.preventDefault();
    const name = this.newName().trim();
    if (!name) return;
    await this.run(async () => {
      await this.api.createAccount({ name, type: this.newType() });
      this.newName.set('');
    });
  }

  protected startEdit(a: Account) {
    this.balanceDraft.set(null);
    this.editing.set({ id: a.id, name: a.name, type: a.type });
  }

  /** Changes one field of the open edit form. */
  protected setEdit(change: Partial<{ name: string; type: AccountType }>) {
    const draft = this.editing();
    if (draft) this.editing.set({ ...draft, ...change });
  }

  protected async saveEdit(a: Account, event: Event) {
    event.preventDefault();
    const draft = this.editing();
    if (!draft || !draft.name.trim()) return;
    await this.run(async () => {
      await this.api.updateAccount(a.id, {
        name: draft.name.trim(),
        type: draft.type,
        archived: a.archived,
      });
      this.editing.set(null);
    });
  }

  protected setArchived(a: Account, archived: boolean) {
    return this.run(() => this.api.updateAccount(a.id, { name: a.name, type: a.type, archived }));
  }

  protected async remove(a: Account) {
    const t = this.i18n.t().planner.accountsPage;
    if (!confirm(t.confirmDelete(a.name))) return;
    await this.run(() => this.api.deleteAccount(a.id), t.inUse);
  }

  /** Runs a write, reloads the list and shows a message if it fails (409 has its own text). */
  private async run(action: () => Promise<unknown>, conflictMessage?: string) {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      this.accounts.reload();
    } catch (e) {
      const conflict = e instanceof HttpErrorResponse && e.status === 409 && conflictMessage;
      this.error.set(conflict || this.i18n.t().planner.error);
    } finally {
      this.busy.set(false);
    }
  }
}
