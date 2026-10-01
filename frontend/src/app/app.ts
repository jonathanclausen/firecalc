import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { Auth } from './core/auth/auth';
import { I18n } from './core/i18n/i18n';
import { CURRENCIES, CurrencySettings } from './core/settings/currency';
import { BrandMark } from './shared/brand-mark';
import { LangSwitch } from './shared/lang-switch';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, BrandMark, LangSwitch],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly currency = inject(CurrencySettings);
  protected readonly currencies = CURRENCIES;
  protected readonly i18n = inject(I18n);
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  /**
   * Signed in and inside "My finances": the planner draws its own app frame,
   * so the public header and footer step aside.
   */
  protected readonly appShell = computed(
    () => this.auth.signedIn() && /^\/planner(\/|\?|$)/.test(this.url()),
  );
}
