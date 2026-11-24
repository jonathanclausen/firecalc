import { Component, input } from '@angular/core';

let nextId = 0;

/**
 * Bolle, the snowball mascot on the front page. Pure SVG so it renders on the server,
 * scales freely and animates with CSS only. `calm` drops the sparkles and spinning coin
 * for small placements.
 */
@Component({
  selector: 'app-mascot',
  host: { '[class.calm]': 'calm()' },
  template: `
    <svg viewBox="0 0 220 220" role="img" [attr.aria-label]="label()">
      <defs>
        <radialGradient [attr.id]="id + 'b'" cx="38%" cy="32%" r="70%">
          <stop offset="0" stop-color="#fff" />
          <stop offset=".55" stop-color="var(--l-soft)" />
          <stop offset="1" stop-color="var(--l-ball)" />
        </radialGradient>
        <linearGradient [attr.id]="id + 'i'" x1="0" y1="0" x2="0" y2="1">
          <stop offset=".35" stop-color="var(--l-ink)" />
          <stop offset="1" stop-color="var(--l-accent)" />
        </linearGradient>
      </defs>
      <g class="sparkles">
        <path d="M24 40l4 10 10 4-10 4-4 10-4-10-10-4 10-4z" fill="var(--l-accent2)" />
        <path d="M192 30l3 7 7 3-7 3-3 7-3-7-7-3 7-3z" fill="var(--l-accent)" />
        <path d="M200 120l2 5 5 2-5 2-2 5-2-5-5-2 5-2z" fill="var(--l-accent2)" />
      </g>
      <ellipse cx="110" cy="208" rx="62" ry="8" fill="var(--l-ink)" opacity=".12" />
      <g class="body">
        <circle
          cx="110"
          cy="118"
          r="84"
          [attr.fill]="'url(#' + id + 'b)'"
          stroke="var(--l-ink)"
          stroke-width="5"
        />
        <circle cx="70" cy="70" r="5" fill="#fff" opacity=".9" />
        <ellipse cx="80" cy="112" rx="17" ry="22" [attr.fill]="'url(#' + id + 'i)'" />
        <ellipse cx="140" cy="112" rx="17" ry="22" [attr.fill]="'url(#' + id + 'i)'" />
        <circle cx="74" cy="103" r="7" fill="#fff" />
        <circle cx="134" cy="103" r="7" fill="#fff" />
        <circle cx="86" cy="122" r="3.2" fill="#fff" />
        <circle cx="146" cy="122" r="3.2" fill="#fff" />
        <path
          d="M62 86q10-8 22-4M136 82q12-4 22 4"
          stroke="var(--l-ink)"
          stroke-width="4"
          fill="none"
          stroke-linecap="round"
        />
        <ellipse cx="58" cy="140" rx="12" ry="6.5" fill="#ff9ec7" opacity=".75" />
        <ellipse cx="162" cy="140" rx="12" ry="6.5" fill="#ff9ec7" opacity=".75" />
        <path
          d="M98 140q6 7 12 0q6 7 12 0"
          stroke="var(--l-ink)"
          stroke-width="4"
          fill="none"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
        <path
          d="M34 160q76 36 152 0l2 16q-78 38-156 0z"
          fill="var(--l-accent)"
          stroke="var(--l-ink)"
          stroke-width="4"
          stroke-linejoin="round"
        />
        <path
          d="M150 176l14 34 16-8-12-30z"
          fill="var(--l-accent)"
          stroke="var(--l-ink)"
          stroke-width="4"
          stroke-linejoin="round"
        />
        <g class="coin">
          <circle cx="188" cy="128" r="20" fill="#fff" stroke="var(--l-ink)" stroke-width="4" />
          <text
            x="188"
            y="135"
            text-anchor="middle"
            font-weight="800"
            font-size="18"
            fill="var(--l-accent)"
          >
            kr
          </text>
        </g>
      </g>
    </svg>
  `,
  styles: `
    :host {
      display: block;
    }
    svg {
      display: block;
      width: 100%;
      height: auto;
      overflow: visible;
    }
    .body {
      transform-origin: 110px 205px;
      animation: hop 1.6s cubic-bezier(0.5, 0, 0.5, 1) infinite;
    }
    .sparkles {
      animation: twinkle 1.2s steps(2) infinite;
    }
    .coin {
      transform-origin: 188px 128px;
      animation: spin 3s linear infinite;
    }
    :host(.calm) .sparkles,
    :host(.calm) .coin {
      display: none;
    }
    @keyframes hop {
      0%,
      100% {
        transform: translateY(0) scale(1.04, 0.96);
      }
      40% {
        transform: translateY(-18px) scale(0.97, 1.03);
      }
      60% {
        transform: translateY(-20px) scale(0.98, 1.02);
      }
    }
    @keyframes twinkle {
      50% {
        opacity: 0.2;
      }
    }
    @keyframes spin {
      0%,
      100% {
        transform: scaleX(1);
      }
      50% {
        transform: scaleX(0.2);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      .body,
      .sparkles,
      .coin {
        animation: none;
      }
    }
  `,
})
export class Mascot {
  readonly calm = input(false);
  readonly label = input('Bolle');
  protected readonly id = `bolle${nextId++}`;
}
