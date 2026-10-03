import { NgTemplateOutlet } from '@angular/common';
import {
  Component,
  computed,
  DestroyRef,
  ElementRef,
  afterNextRender,
  effect,
  inject,
  viewChild,
} from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth } from '../../core/auth/auth';
import { I18n } from '../../core/i18n/i18n';
import { BrandMark } from '../../shared/brand-mark';
import { LangSwitch } from '../../shared/lang-switch';

type NavLabel = 'overview' | 'portfolio' | 'accounts' | 'goal';

/** Frame for the "My finances" pages: sign-in gate, then an app shell (sidebar, or app bar and tab bar on phones). */
@Component({
  selector: 'app-planner-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NgTemplateOutlet, BrandMark, LangSwitch],
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
  private readonly button = viewChild<ElementRef<HTMLElement>>('googleButton');

  constructor() {
    this.i18n.pageTitle.set((t) => t.planner.pageTitle);
    inject(DestroyRef).onDestroy(() => this.i18n.pageTitle.set((t) => t.pageTitle));

    afterNextRender(() => void this.auth.start());

    // Redraw Google's button when it appears or the language changes.
    effect(() => {
      const el = this.button()?.nativeElement;
      const locale = this.i18n.lang();
      if (!el) return;
      el.replaceChildren();
      void this.auth.renderButton(el, locale);
    });
  }
}
