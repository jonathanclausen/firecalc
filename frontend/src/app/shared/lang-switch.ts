import { Component, inject } from '@angular/core';
import { I18n } from '../core/i18n/i18n';

/** DA/EN toggle, used in the public header and in the signed-in app. */
@Component({
  selector: 'app-lang-switch',
  template: `
    <div class="lang" role="group" [attr.aria-label]="i18n.t().language">
      @for (l of i18n.languages; track l.code) {
        <button
          type="button"
          [class.active]="i18n.lang() === l.code"
          [attr.aria-pressed]="i18n.lang() === l.code"
          [attr.lang]="l.code"
          [title]="l.label"
          (click)="i18n.set(l.code)"
        >
          {{ l.code.toUpperCase() }}
        </button>
      }
    </div>
  `,
  styles: `
    .lang {
      display: flex;
      padding: 3px;
      gap: 2px;
      background: var(--surface-input);
      border: 1px solid var(--border);
      border-radius: 8px;
    }

    button {
      padding: 4px 9px;
      border: 0;
      border-radius: 6px;
      background: none;
      font: inherit;
      font-size: 0.75rem;
      font-weight: 600;
      letter-spacing: 0.04em;
      color: var(--text-muted);
      cursor: pointer;

      &.active {
        background: var(--surface-card);
        color: var(--text-primary);
        box-shadow: var(--shadow-sm);
      }

      &:focus-visible {
        outline: 2px solid var(--accent-ring);
      }
    }
  `,
})
export class LangSwitch {
  protected readonly i18n = inject(I18n);
}
