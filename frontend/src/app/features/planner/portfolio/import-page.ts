import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Account, ImportResult, PlannerApi } from '../../../core/api/planner-api';
import { Auth } from '../../../core/auth/auth';
import { I18n } from '../../../core/i18n/i18n';
import { CurrencySettings } from '../../../core/settings/currency';

/** Uploads a Nordnet transaction export: preview first, then import (/planner/portfolio/import). */
@Component({
  selector: 'app-import-page',
  imports: [RouterLink],
  templateUrl: './import-page.html',
  styleUrl: './import-page.scss',
})
export class ImportPage {
  protected readonly i18n = inject(I18n);
  private readonly currencySettings = inject(CurrencySettings);
  private readonly api = inject(PlannerApi);
  private readonly auth = inject(Auth);

  protected readonly accounts = httpResource<Account[]>(() => '/api/accounts');
  protected readonly investmentAccounts = computed(() =>
    (this.accounts.value() ?? []).filter((a) => a.type === 'investment'),
  );

  protected readonly accountId = signal('');
  protected readonly file = signal<{ name: string; data: ArrayBuffer } | null>(null);
  protected readonly preview = signal<ImportResult | null>(null);
  protected readonly done = signal<ImportResult | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const first = this.investmentAccounts()[0];
      if (first && !this.accountId()) this.accountId.set(first.id);
    });
  }

  protected chooseAccount(id: string) {
    this.accountId.set(id);
    this.preview.set(null);
    this.done.set(null);
  }

  protected async chooseFile(input: HTMLInputElement) {
    const f = input.files?.[0];
    this.preview.set(null);
    this.done.set(null);
    this.file.set(f ? { name: f.name, data: await f.arrayBuffer() } : null);
    if (f) await this.send(false);
  }

  protected send(commit: boolean) {
    const file = this.file();
    if (!file || !this.accountId()) return;
    return this.run(async () => {
      const result = await this.api.importNordnet(this.accountId(), file.data, commit);
      if (commit) {
        this.done.set(result);
        this.preview.set(null);
        this.file.set(null);
      } else {
        this.preview.set(result);
      }
    });
  }

  protected amount(value: number) {
    return this.currencySettings.format(value, { currency: this.auth.user()?.currency ?? 'DKK' });
  }

  private async run(action: () => Promise<void>) {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (e) {
      const t = this.i18n.t().planner;
      const bad = e instanceof HttpErrorResponse && e.status === 400;
      this.error.set(bad ? t.importPage.badFile : t.error);
    } finally {
      this.busy.set(false);
    }
  }
}
