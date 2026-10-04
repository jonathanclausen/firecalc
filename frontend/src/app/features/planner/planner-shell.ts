import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, DestroyRef, afterNextRender, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth } from '../../core/auth/auth';
import { I18n } from '../../core/i18n/i18n';
import { BrandMark } from '../../shared/brand-mark';
import { LangSwitch } from '../../shared/lang-switch';
import { SignInPanel } from './sign-in/sign-in-panel';

type NavLabel = 'overview' | 'portfolio' | 'accounts' | 'goal';

/** Frame for the "My finances" pages: sign-in gate, then an app shell (sidebar, or app bar and tab bar on phones). */
@Component({
  selector: 'app-planner-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    NgTemplateOutlet,
    BrandMark,
    LangSwitch,
    SignInPanel,
  ],
  templateUrl: './planner-shell.html',
  styleUrl: './planner-shell.scss',
  host: { class: 'planner' },
})
export class PlannerShell {
  protected readonly auth = inject(Auth);
  protected readonly i18n = inject(I18n);
  protected readonly nav: { path: string; label: NavLabel; icon: string; exact: boolean }[] = [
    { path: '/planner', label: 'overview', icon: 'overview', exact: true },
    { path: '/planner/portfolio', label: 'portfolio', icon: 'portfolio', exact: false },
    { path: '/planner/accounts', label: 'accounts', icon: 'accounts', exact: false },
    { path: '/planner/goal', label: 'goal', icon: 'goal', exact: false },
  ];
  protected readonly initial = computed(() =>
    (this.auth.user()?.name || this.auth.user()?.email || '?').charAt(0).toUpperCase(),
  );
  protected readonly busy = signal(false);
  protected readonly verifyMessage = signal<string | null>(null);

  constructor() {
    this.i18n.pageTitle.set((t) => t.planner.pageTitle);
    inject(DestroyRef).onDestroy(() => this.i18n.pageTitle.set((t) => t.pageTitle));

    afterNextRender(() => void this.auth.start());
  }

  protected async checkVerified() {
    await this.whileBusy(() => this.auth.checkVerified());
    if (this.auth.state() === 'verifyEmail') {
      this.verifyMessage.set(this.i18n.t().planner.signIn.verifyNotYet);
    }
  }

  protected async resend() {
    await this.whileBusy(() => this.auth.resendVerification());
    this.verifyMessage.set(this.i18n.t().planner.signIn.verifyResent);
  }

  private async whileBusy(action: () => Promise<void>) {
    this.busy.set(true);
    this.verifyMessage.set(null);
    try {
      await action();
    } catch {
      this.verifyMessage.set(this.i18n.t().planner.signIn.errors.tooMany);
    } finally {
      this.busy.set(false);
    }
  }
}
