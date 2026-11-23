import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { OAuthClientInfo, PlannerApi } from '../../../core/api/planner-api';
import { I18n } from '../../../core/i18n/i18n';

/**
 * The approval page an app such as Claude sends the person to when it connects over MCP (the OAuth
 * authorization endpoint). The planner shell signs the person in first; this page shows who is asking and
 * where the answer goes, and the API checks the app and its return address before handing out a code.
 */
@Component({
  selector: 'app-connect-page',
  templateUrl: './connect-page.html',
  styleUrl: './connect-page.scss',
})
export class ConnectPage {
  protected readonly i18n = inject(I18n);
  private readonly api = inject(PlannerApi);
  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;

  private readonly clientId = this.query.get('client_id');
  private readonly redirectUri = this.query.get('redirect_uri');

  protected readonly client = httpResource<OAuthClientInfo>(() =>
    this.clientId && this.redirectUri
      ? {
          url: '/api/oauth/client',
          params: { clientId: this.clientId, redirectUri: this.redirectUri },
        }
      : undefined,
  );
  protected readonly invalid = computed(
    () => !this.clientId || !this.redirectUri || !!this.client.error(),
  );
  protected readonly busy = signal(false);
  protected readonly leaving = signal(false);
  protected readonly failed = signal(false);

  protected async answer(approve: boolean) {
    this.busy.set(true);
    this.failed.set(false);
    try {
      const { redirectUrl } = await this.api.authorizeApp({
        clientId: this.clientId,
        redirectUri: this.redirectUri,
        responseType: this.query.get('response_type'),
        codeChallenge: this.query.get('code_challenge'),
        codeChallengeMethod: this.query.get('code_challenge_method'),
        state: this.query.get('state'),
        approve,
      });
      this.leaving.set(true);
      window.location.assign(redirectUrl);
    } catch {
      this.failed.set(true);
      this.busy.set(false);
    }
  }
}
