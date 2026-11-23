import { httpResource } from '@angular/common/http';
import { DOCUMENT, Component, inject, signal } from '@angular/core';
import { AppConnection, PlannerApi } from '../../../core/api/planner-api';
import { Auth, AuthErrorKey, authErrorKey } from '../../../core/auth/auth';
import { I18n } from '../../../core/i18n/i18n';

/**
 * "Din konto": which logins reach this account, linking another one, apps connected over MCP, and deleting
 * everything.
 */
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

  private readonly api = inject(PlannerApi);
  protected readonly connections = httpResource<AppConnection[]>(() => '/api/oauth/connections');
  /** The address an MCP client such as Claude connects to: this site's /mcp. */
  protected readonly mcpUrl = (inject(DOCUMENT).location?.origin ?? '') + '/mcp';
  protected readonly copied = signal(false);

  protected async copy() {
    await navigator.clipboard.writeText(this.mcpUrl);
    this.copied.set(true);
    setTimeout(() => this.copied.set(false), 2000);
  }

  protected async disconnect(app: AppConnection) {
    if (!confirm(this.i18n.t().planner.accountPage.confirmDisconnect(app.clientName))) return;
    this.busy.set(true);
    try {
      await this.api.disconnectApp(app.id);
      this.connections.reload();
    } finally {
      this.busy.set(false);
    }
  }

  /** An ISO timestamp as a date in the chosen language. */
  protected day(timestamp: string) {
    return this.i18n.date(timestamp.slice(0, 10));
  }

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
