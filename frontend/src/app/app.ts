import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { I18n } from './core/i18n/i18n';
import { CURRENCIES, CurrencySettings } from './core/settings/currency';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly currency = inject(CurrencySettings);
  protected readonly currencies = CURRENCIES;
  protected readonly i18n = inject(I18n);
}
