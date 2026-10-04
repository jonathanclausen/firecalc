import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Account, PlannerApi, Scenario } from '../../../core/api/planner-api';
import { Auth } from '../../../core/auth/auth';
import { I18n } from '../../../core/i18n/i18n';

interface Item {
  key: string;
  link: string;
  done: boolean;
}

/**
 * "Kom godt i gang · 3 af 5" on the overview. Each item ticks itself off from the user's own data,
 * and the card goes away when all are done or it is hidden.
 */
@Component({
  selector: 'app-getting-started',
  imports: [RouterLink],
  template: `
    @let c = i18n.t().planner.checklist;
    @if (visible()) {
      <section class="card checklist" [attr.aria-label]="c.title">
        <div class="checklist__header">
          <div>
            <h2>{{ c.title }}</h2>
            <span class="checklist__progress">{{ c.progress(doneCount(), items().length) }}</span>
          </div>
          <button type="button" class="link-button" [disabled]="hiding()" (click)="hide()">
            {{ c.hide }}
          </button>
        </div>
        <div
          class="checklist__bar"
          role="progressbar"
          aria-valuemin="0"
          [attr.aria-valuemax]="items().length"
          [attr.aria-valuenow]="doneCount()"
          [attr.aria-label]="c.title"
        >
          <span [style.width.%]="(doneCount() / items().length) * 100"></span>
        </div>
        <ul class="checklist__items">
          @for (item of items(); track item.key) {
            <li [class.done]="item.done">
              <a [routerLink]="item.link">
                <span class="checklist__tick" aria-hidden="true">
                  @if (item.done) {
                    <svg viewBox="0 0 16 16"><path d="M3.5 8.5l3 3 6-7" /></svg>
                  }
                </span>
                <span>
                  <strong>{{ c.items[item.key].title }}</strong>
                  <small>{{ c.items[item.key].text }}</small>
                </span>
              </a>
            </li>
          }
        </ul>
        <a class="link-button" routerLink="/planner/welcome">{{ c.reopen }}</a>
      </section>
    }
  `,
  styles: `
    .checklist {
      display: flex;
      flex-direction: column;
      gap: 14px;
      margin-bottom: 20px;
    }

    .checklist__header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 12px;

      h2 {
        margin: 0 0 2px;
        font-size: 1.0625rem;
      }
    }

    .checklist__progress {
      font-size: 0.8125rem;
      color: var(--text-muted);
    }

    .checklist__bar {
      height: 6px;
      border-radius: 999px;
      background: var(--track);
      overflow: hidden;

      span {
        display: block;
        height: 100%;
        border-radius: inherit;
        background: var(--accent);
        transition: width 0.3s;
      }
    }

    .checklist__items {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
      gap: 8px;
      margin: 0;
      padding: 0;
      list-style: none;

      a {
        display: flex;
        gap: 10px;
        height: 100%;
        box-sizing: border-box;
        padding: 12px;
        border: 1px solid var(--border);
        border-radius: var(--radius-sm);
        color: inherit;
        text-decoration: none;

        &:hover {
          border-color: var(--accent);
          background: var(--surface-hover);
        }
      }

      strong {
        display: block;
        font-size: 0.875rem;
      }

      small {
        font-size: 0.75rem;
        color: var(--text-muted);
      }

      .done strong {
        color: var(--text-muted);
        text-decoration: line-through;
      }
    }

    .checklist__tick {
      display: inline-flex;
      flex-shrink: 0;
      align-items: center;
      justify-content: center;
      width: 20px;
      height: 20px;
      margin-top: 1px;
      border: 1.5px solid var(--border);
      border-radius: 50%;

      .done & {
        border-color: var(--accent);
        background: var(--accent);
        color: var(--surface-card);
      }

      svg {
        width: 12px;
        height: 12px;
        fill: none;
        stroke: currentColor;
        stroke-width: 2.2;
        stroke-linecap: round;
        stroke-linejoin: round;
      }
    }

    .checklist > .link-button {
      align-self: flex-start;
    }
  `,
})
export class GettingStarted {
  protected readonly i18n = inject(I18n);
  private readonly auth = inject(Auth);
  private readonly api = inject(PlannerApi);

  readonly accounts = input.required<Account[]>();
  readonly hasGoal = input.required<boolean>();

  private readonly scenarios = httpResource<Scenario[]>(() => '/api/scenarios');
  protected readonly hiding = signal(false);

  protected readonly items = computed<Item[]>(() => {
    const accounts = this.accounts();
    return [
      { key: 'account', link: '/planner/accounts', done: accounts.length > 0 },
      {
        key: 'import',
        link: '/planner/portfolio/import',
        // Nothing to import without a share account.
        done:
          accounts.some((a) => a.tracked) ||
          (accounts.length > 0 && !accounts.some((a) => a.type === 'investment')),
      },
      { key: 'goal', link: '/planner/goal', done: this.hasGoal() },
      { key: 'profile', link: '/planner/future', done: !!this.auth.user()?.birthDate },
      {
        key: 'scenario',
        link: '/planner/future',
        done: (this.scenarios.value()?.length ?? 0) > 0,
      },
    ];
  });

  protected readonly doneCount = computed(() => this.items().filter((i) => i.done).length);

  protected readonly visible = computed(() => {
    const user = this.auth.user();
    return (
      !!user &&
      !user.checklistHidden &&
      !user.hasDemo &&
      this.scenarios.hasValue() &&
      this.doneCount() < this.items().length
    );
  });

  protected async hide() {
    this.hiding.set(true);
    try {
      this.auth.user.set(await this.api.saveOnboarding({ checklistHidden: true }));
    } finally {
      this.hiding.set(false);
    }
  }
}
