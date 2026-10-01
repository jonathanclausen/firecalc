import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { ACCOUNT_TYPES, Account, AccountType, PlannerApi } from '../../../core/api/planner-api';
import { I18n } from '../../../core/i18n/i18n';

@Component({
  selector: 'app-accounts-page',
  templateUrl: './accounts-page.html',
  styleUrl: './accounts-page.scss',
})
export class AccountsPage {
  protected readonly i18n = inject(I18n);
  private readonly api = inject(PlannerApi);

  protected readonly types = ACCOUNT_TYPES;
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
  protected readonly editing = signal<{ id: string; name: string; type: AccountType } | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

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
    this.editing.set({ id: a.id, name: a.name, type: a.type });
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
