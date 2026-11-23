import { Component, computed, inject, input } from '@angular/core';
import { I18n } from '../../../../core/i18n/i18n';
import { CurrencySettings } from '../../../../core/settings/currency';
import { SecurityRow, logo } from './portfolio-period';

/** One security in an app-style list: icon, name, count and value, and the period's gain. */
@Component({
  selector: 'app-security-row',
  template: `
    @let r = row();
    @let h = i18n.t().planner.portfolioPage.history;
    <span class="logo" [style.background]="icon().color" aria-hidden="true">{{
      icon().letters
    }}</span>
    <span class="main">
      <strong>{{ r.name }}</strong>
      <small>
        @if (r.sold) {
          {{ h.soldInPeriod }}
        } @else {
          {{ h.pieces(i18n.number(r.quantity, 4)) }} · {{ money(r.value) }}
        }
        @if (showAccounts() && r.accounts.length) {
          · {{ r.accounts.join(' + ') }}
        }
      </small>
    </span>
    <span class="gain" [class]="tone()">
      <strong>{{ signedMoney(r.gain) }}</strong>
      @if (r.gainPct !== null) {
        <small>{{ signedPct(r.gainPct) }}</small>
      }
    </span>
    <svg class="chev" viewBox="0 0 20 20" aria-hidden="true">
      <path d="M8 5l5 5-5 5" fill="none" stroke="currentColor" stroke-width="2" />
    </svg>
  `,
  styles: `
    :host {
      display: flex;
      align-items: center;
      gap: 12px;
      min-height: 64px;
      padding: 10px 14px;
    }
    .logo {
      flex: 0 0 auto;
      display: grid;
      place-items: center;
      width: 40px;
      height: 40px;
      border-radius: 12px;
      color: #fff;
      font-size: 0.75rem;
      font-weight: 800;
      letter-spacing: 0.02em;
    }
    .main {
      flex: 1;
      min-width: 0;
      display: flex;
      flex-direction: column;
      gap: 2px;

      strong {
        font-size: 0.9375rem;
        font-weight: 600;
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }
      small {
        font-size: 0.8125rem;
        color: var(--text-muted);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
      }
    }
    .gain {
      display: flex;
      flex-direction: column;
      align-items: flex-end;
      gap: 2px;
      font-variant-numeric: tabular-nums;
      white-space: nowrap;

      strong {
        font-size: 0.9375rem;
        font-weight: 600;
      }
      small {
        font-size: 0.8125rem;
        color: var(--text-muted);
      }
      &.up strong {
        color: var(--positive);
      }
      &.down strong {
        color: var(--danger);
      }
    }
    .chev {
      flex: 0 0 auto;
      width: 16px;
      height: 16px;
      margin-right: -4px;
      color: var(--text-muted);
      opacity: 0.6;
    }
  `,
})
export class SecurityRowView {
  protected readonly i18n = inject(I18n);
  private readonly currencySettings = inject(CurrencySettings);

  readonly row = input.required<SecurityRow>();
  readonly currency = input('DKK');
  readonly showAccounts = input(false);

  protected readonly icon = computed(() => logo(this.row().name, this.row().symbol));
  protected readonly tone = computed(() => {
    const g = Math.round(this.row().gain);
    return g > 0 ? 'up' : g < 0 ? 'down' : '';
  });

  protected money(value: number) {
    return this.currencySettings.format(value, { currency: this.currency() });
  }

  protected signedMoney(value: number) {
    const rounded = Math.round(value);
    return (rounded > 0 ? '+' : '') + this.money(rounded === 0 ? 0 : value);
  }

  protected signedPct(value: number) {
    return (value > 0 ? '+' : '') + this.i18n.percent(value, 1);
  }
}
