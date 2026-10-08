import { Component, inject, signal } from '@angular/core';
import { Auth, AuthErrorKey, authErrorKey } from '../../../core/auth/auth';
import { I18n } from '../../../core/i18n/i18n';

type Mode = 'signIn' | 'signUp' | 'reset';
type Action = 'google' | 'facebook' | 'form';

/** The signed-out gate: Google, Facebook, or email and password, plus sign-up and password reset. */
@Component({
  selector: 'app-sign-in-panel',
  templateUrl: './sign-in-panel.html',
  styleUrl: './sign-in-panel.scss',
})
export class SignInPanel {
  protected readonly auth = inject(Auth);
  protected readonly i18n = inject(I18n);

  protected readonly mode = signal<Mode>('signIn');
  protected readonly email = signal('');
  protected readonly password = signal('');
  /** The button whose sign-in is running; every button is disabled meanwhile. */
  protected readonly busy = signal<Action | null>(null);
  protected readonly error = signal<AuthErrorKey | null>(null);
  protected readonly resetSentTo = signal<string | null>(null);

  protected setMode(mode: Mode) {
    this.mode.set(mode);
    this.error.set(null);
    this.resetSentTo.set(null);
  }

  protected google() {
    return this.run('google', () => this.auth.signInWithGoogle());
  }

  protected facebook() {
    return this.run('facebook', () => this.auth.signInWithFacebook());
  }

  protected submit(event: Event) {
    event.preventDefault();
    const email = this.email();
    switch (this.mode()) {
      case 'signIn':
        return this.run('form', () => this.auth.signInWithPassword(email, this.password()));
      case 'signUp':
        return this.run('form', () => this.auth.signUp(email, this.password()));
      case 'reset':
        return this.run('form', async () => {
          await this.auth.resetPassword(email);
          this.resetSentTo.set(email.trim());
        });
    }
  }

  protected providerName(id: string) {
    return this.i18n.t().planner.accountPage.providers[id] ?? id;
  }

  private async run(name: Action, action: () => Promise<void>) {
    this.busy.set(name);
    this.error.set(null);
    try {
      await action();
    } catch (error) {
      this.error.set(authErrorKey(error));
    } finally {
      this.busy.set(null);
    }
  }
}
