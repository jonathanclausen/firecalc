import { Component, inject } from '@angular/core';
import { I18n } from '../../../../core/i18n/i18n';
import { PortfolioPeriod, RANGES } from './portfolio-period';

/** The 1M … Alt pills, with dates of one's own behind "Vælg datoer". */
@Component({
  selector: 'app-period-picker',
  template: `
    @let h = i18n.t().planner.portfolioPage.history;
    <div class="ranges" role="radiogroup" [attr.aria-label]="h.range">
      @for (r of ranges; track r) {
        <button
          type="button"
          role="radio"
          [attr.aria-checked]="period.range() === r"
          [class.active]="period.range() === r"
          (click)="period.pick(r)"
        >
          {{ h.ranges[r] }}
        </button>
      }
      <button
        type="button"
        role="radio"
        [attr.aria-checked]="period.range() === 'custom'"
        [class.active]="period.range() === 'custom'"
        [attr.aria-label]="h.pickDates"
        (click)="period.pick('custom')"
      >
        <svg viewBox="0 0 20 20" aria-hidden="true">
          <rect
            x="3"
            y="4.5"
            width="14"
            height="12"
            rx="2"
            fill="none"
            stroke="currentColor"
            stroke-width="1.6"
          />
          <path d="M3 8.5h14M7 2.5v3M13 2.5v3" stroke="currentColor" stroke-width="1.6" />
        </svg>
      </button>
    </div>
    @if (period.range() === 'custom') {
      <div class="dates">
        <label class="form-field">
          <span>{{ h.from }}</span>
          <input
            type="date"
            [value]="period.history.value()?.from ?? period.customFrom()"
            [max]="period.customTo() || period.today"
            (change)="period.setFrom($any($event.target).value)"
          />
        </label>
        <label class="form-field">
          <span>{{ h.to }}</span>
          <input
            type="date"
            [value]="period.history.value()?.to ?? period.customTo()"
            [max]="period.today"
            (change)="period.setTo($any($event.target).value)"
          />
        </label>
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .ranges {
      display: flex;
      justify-content: space-between;
      gap: 2px;
    }
    button {
      flex: 0 1 auto;
      min-width: 40px;
      min-height: 36px;
      padding: 6px 10px;
      border: 0;
      border-radius: 999px;
      background: transparent;
      font: inherit;
      font-size: 0.875rem;
      font-weight: 600;
      color: var(--text-muted);
      cursor: pointer;

      &.active {
        background: var(--text-primary);
        color: var(--surface-card);
      }

      &:focus-visible {
        outline: 2px solid var(--accent-ring);
      }

      svg {
        display: block;
        width: 18px;
        height: 18px;
      }
    }
    .dates {
      display: flex;
      gap: 8px;
      margin-top: 12px;

      label {
        flex: 1;
      }
    }
  `,
})
export class PeriodPicker {
  protected readonly i18n = inject(I18n);
  protected readonly period = inject(PortfolioPeriod);
  protected readonly ranges = RANGES;
}
