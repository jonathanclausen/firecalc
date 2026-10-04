import { Component, ElementRef, inject, input, signal } from '@angular/core';

let nextId = 0;

/**
 * A small (i) next to a word that needs explaining, e.g. friværdi or GAK. Opens a short note on
 * click or tap (hover alone doesn't work on phones); Escape, a click elsewhere or leaving it closes it.
 */
@Component({
  selector: 'app-help-tip',
  template: `
    <button
      type="button"
      class="help-tip__button"
      [attr.aria-label]="label()"
      [attr.aria-expanded]="open()"
      [attr.aria-describedby]="open() ? id : null"
      (click)="toggle($event)"
      (keydown.escape)="open.set(false)"
      (blur)="open.set(false)"
    >
      <svg viewBox="0 0 16 16" aria-hidden="true">
        <circle cx="8" cy="8" r="6.5" />
        <path d="M8 7.2v4M8 4.8v.01" />
      </svg>
    </button>
    @if (open()) {
      <span class="help-tip__bubble" role="tooltip" [id]="id">
        @if (title()) {
          <strong>{{ title() }}</strong>
        }
        {{ text() }}
      </span>
    }
  `,
  styles: `
    :host {
      position: relative;
      display: inline-flex;
      vertical-align: middle;
      margin-left: 4px;
    }

    .help-tip__button {
      display: inline-flex;
      padding: 2px;
      border: 0;
      border-radius: 50%;
      background: none;
      color: var(--text-muted);
      cursor: pointer;

      &:hover,
      &[aria-expanded='true'] {
        color: var(--accent);
      }

      &:focus-visible {
        outline: none;
        box-shadow: 0 0 0 3px var(--accent-ring);
      }

      svg {
        width: 16px;
        height: 16px;
        fill: none;
        stroke: currentColor;
        stroke-width: 1.4;
        stroke-linecap: round;
      }
    }

    .help-tip__bubble {
      position: absolute;
      z-index: 20;
      top: calc(100% + 6px);
      left: 50%;
      transform: translateX(-50%);
      width: max-content;
      max-width: min(280px, 80vw);
      padding: 10px 12px;
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      background: var(--surface-raised);
      box-shadow: var(--shadow-md);
      color: var(--text-secondary);
      font-size: 0.8125rem;
      font-weight: 400;
      line-height: 1.45;
      text-align: left;
      text-transform: none;
      letter-spacing: normal;
      white-space: normal;

      strong {
        display: block;
        margin-bottom: 2px;
        color: var(--text-primary);
      }
    }
  `,
  host: { '(document:click)': 'outside($event)' },
})
export class HelpTip {
  /** What the term means, in a sentence or two. */
  readonly text = input.required<string>();
  /** The term itself, shown in bold above the text. */
  readonly title = input<string>('');
  /** Read out for the button, e.g. "Hvad er friværdi?". */
  readonly label = input.required<string>();

  protected readonly open = signal(false);
  protected readonly id = `help-tip-${nextId++}`;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected toggle(event: Event) {
    // Inside a <label> a click would also toggle or focus the field it labels.
    event.preventDefault();
    event.stopPropagation();
    this.open.update((v) => !v);
  }

  protected outside(event: Event) {
    if (this.open() && !this.host.contains(event.target as Node)) this.open.set(false);
  }
}
