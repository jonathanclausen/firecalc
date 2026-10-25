import { NgTemplateOutlet } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import {
  Component,
  computed,
  DestroyRef,
  afterNextRender,
  effect,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { PlannerApi } from '../../core/api/planner-api';
import { Auth } from '../../core/auth/auth';
import { I18n } from '../../core/i18n/i18n';
import { BrandMark } from '../../shared/brand-mark';
import { LangSwitch } from '../../shared/lang-switch';
import { SignInPanel } from './sign-in/sign-in-panel';

type NavLabel = 'overview' | 'portfolio' | 'accounts' | 'home' | 'future';

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
  host: { class: 'planner', '(document:keydown.escape)': 'menuOpen.set(false)' },
})
export class PlannerShell {
  protected readonly auth = inject(Auth);
  protected readonly i18n = inject(I18n);
  protected readonly nav: { path: string; label: NavLabel; icon: string; exact: boolean }[] = [
    { path: '/planner', label: 'overview', icon: 'overview', exact: true },
    { path: '/planner/portfolio', label: 'portfolio', icon: 'portfolio', exact: false },
    { path: '/planner/accounts', label: 'accounts', icon: 'accounts', exact: false },
    { path: '/planner/home', label: 'home', icon: 'home', exact: false },
    { path: '/planner/future', label: 'future', icon: 'future', exact: false },
  ];
  /** The phone tab bar; the rest sits behind "Mere". */
  protected readonly tabs = this.nav.filter((item) => item.label !== 'accounts');
  protected readonly menuOpen = signal(false);
  /** Pages behind "Mere" on phones, with the tools under their own heading. */
  protected readonly menu = computed(() => {
    const t = this.i18n.t().planner;
    const items: { path: string; label: string; icon: string; heading?: string }[] = [
      { path: '/planner/accounts', label: t.accounts, icon: 'accounts' },
      { path: '/planner/welcome', label: t.guide, icon: 'guide', heading: t.tools },
      { path: '/planner/fire', label: t.fire, icon: 'fire' },
      { path: '/compound-interest', label: this.i18n.t().nav.compoundInterest, icon: 'calculator' },
    ];
    if (this.isAdmin()) items.push({ path: '/planner/admin', label: t.admin.nav, icon: 'admin' });
    return items;
  });
  private readonly url = toSignal(
    inject(Router).events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects),
    ),
    { initialValue: inject(Router).url },
  );
  /** Lights up "Mere" while one of its pages is open. */
  protected readonly inMenu = computed(() =>
    this.menu().some((item) => this.url().startsWith(item.path)),
  );
  protected readonly initial = computed(() =>
    (this.auth.user()?.name || this.auth.user()?.email || '?').charAt(0).toUpperCase(),
  );
  protected readonly busy = signal(false);
  protected readonly verifyMessage = signal<string | null>(null);
  private readonly router = inject(Router);
  private readonly api = inject(PlannerApi);
  protected readonly removingDemo = signal(false);
  /** Shows the "Admin" menu item. The API answers 404 to everyone else. */
  protected readonly isAdmin = signal(false);

  constructor() {
    this.i18n.pageTitle.set((t) => t.planner.pageTitle);
    inject(DestroyRef).onDestroy(() => this.i18n.pageTitle.set((t) => t.pageTitle));

    afterNextRender(() => void this.auth.start());

    const http = inject(HttpClient);
    let adminChecked = false;
    effect(() => {
      if (!this.auth.user() || adminChecked) return;
      adminChecked = true;
      http.get('/api/admin/access').subscribe({
        next: () => this.isAdmin.set(true),
        error: () => this.isAdmin.set(false),
      });
    });

    // A new user starts in the welcome guide, once per visit; they can leave it at any time.
    let guided = false;
    effect(() => {
      const user = this.auth.user();
      if (!user || user.onboarded || guided) return;
      guided = true;
      if (!this.router.url.startsWith('/planner/welcome'))
        void this.router.navigateByUrl('/planner/welcome');
    });
  }

  protected async removeDemo() {
    if (!confirm(this.i18n.t().planner.demo.confirmRemove)) return;
    this.removingDemo.set(true);
    try {
      this.auth.user.set(await this.api.removeDemo());
      // Reload the page shown so it stops showing the example numbers.
      const url = this.router.url;
      await this.router.navigateByUrl('/planner/welcome', { skipLocationChange: true });
      await this.router.navigateByUrl(url);
    } finally {
      this.removingDemo.set(false);
    }
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
