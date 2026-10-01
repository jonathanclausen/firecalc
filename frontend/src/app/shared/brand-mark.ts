import { Component } from '@angular/core';

/** The FireCalc logo mark. */
@Component({
  selector: 'app-brand-mark',
  template: `
    <svg viewBox="0 0 32 32" aria-hidden="true">
      <rect width="32" height="32" rx="9" />
      <path d="M8 22 L13.5 15.5 L17.5 19 L24 10.5" />
      <path d="M19.5 10.5 H24 V15" />
    </svg>
  `,
  styles: `
    :host {
      display: block;
      width: 30px;
      height: 30px;
    }

    svg {
      display: block;
      width: 100%;
      height: 100%;
    }

    rect {
      fill: var(--hero-bg);
    }

    path {
      fill: none;
      stroke: var(--hero-series-interest);
      stroke-width: 2.4;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
  `,
})
export class BrandMark {}
