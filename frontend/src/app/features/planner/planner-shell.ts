import {
  Component,
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

/** Frame for the "My finances" pages: sign-in gate, sub navigation and the active page. */
@Component({
  selector: 'app-planner-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './planner-shell.html',
  styleUrl: './planner-shell.scss',
  host: { class: 'planner' },
})
export class PlannerShell {
  protected readonly auth = inject(Auth);
  protected readonly i18n = inject(I18n);
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
