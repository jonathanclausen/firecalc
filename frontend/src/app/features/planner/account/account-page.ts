import { Component, inject, signal } from '@angular/core';
import { Auth, AuthErrorKey, authErrorKey } from '../../../core/auth/auth';
import { I18n } from '../../../core/i18n/i18n';

/** "Din konto": which logins reach this account, linking another one, and deleting everything. */
@Component({
  selector: 'app-account-page',
  templateUrl: './account-page.html',
  styleUrl: './account-page.scss',
})
export class AccountPage {
  protected readonly auth = inject(Auth);
  protected readonly i18n = inject(I18n);
  protected readonly busy = signal(false);
  protected readonly error = signal<AuthErrorKey | null>(null);
  protected readonly all = ['google.com', 'facebook.com', 'password'] as const;

  protected link(provider: 'google.com' | 'facebook.com') {
    return this.run(() => this.auth.link(provider));
  }

  protected remove() {
    const t = this.i18n.t().planner.accountPage;
    if (!confirm(t.confirmDelete) || !confirm(t.confirmDeleteAgain)) return;
    return this.run(() => this.auth.deleteAccount());
  }

  private async run(action: () => Promise<void>) {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (error) {
      this.error.set(authErrorKey(error));
    } finally {
      this.busy.set(false);
    }
  }
}
