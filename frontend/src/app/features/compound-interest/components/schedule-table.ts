import { Component, inject, input } from '@angular/core';
import { I18n } from '../../../core/i18n/i18n';
import { YearRow } from '../../../core/finance/compound-interest';
import { MoneyPipe } from '../../../shared/money.pipe';

/** Year-by-year breakdown of the projection. */
@Component({
  selector: 'app-schedule-table',
  imports: [MoneyPipe],
  template: `
    @let t = i18n.t().table;
    <div class="scroll">
      <table>
        <thead>
          <tr>
            <th scope="col">{{ t.year }}</th>
            <th scope="col">{{ t.deposits }}</th>
            <th scope="col">{{ t.interest }}</th>
            <th scope="col">{{ t.totalDeposits }}</th>
            <th scope="col">{{ t.totalInterest }}</th>
            <th scope="col">{{ t.balance }}</th>
            <th scope="col">{{ t.real }}</th>
          </tr>
        </thead>
        <tbody>
          @for (row of rows(); track row.year) {
            <tr [class.crossover]="row.year === crossoverYear()">
              <th scope="row">{{ row.year }}</th>
              <td>{{ row.contributions | money }}</td>
              <td class="positive">{{ row.interest | money }}</td>
              <td>{{ row.totalContributions | money }}</td>
              <td>{{ row.totalInterest | money }}</td>
              <td class="strong">{{ row.endBalance | money }}</td>
              <td class="muted">{{ row.realEndBalance | money }}</td>
            </tr>
          }
        </tbody>
      </table>
    </div>
  `,
  styles: `
    .scroll {
      max-height: 420px;
      overflow: auto;
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 0.8125rem;
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }
    thead th {
      position: sticky;
      top: 0;
      z-index: 1;
      background: var(--surface-raised);
      color: var(--text-muted);
      font-weight: 600;
      font-size: 0.75rem;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      text-align: right;
      padding: 10px 14px;
      border-bottom: 1px solid var(--border);
    }
    thead th:first-child,
    tbody th {
      text-align: left;
    }
    tbody th,
    td {
      padding: 9px 14px;
      text-align: right;
      border-bottom: 1px solid var(--grid);
      color: var(--text-secondary);
    }
    tbody th {
      font-weight: 600;
      color: var(--text-primary);
    }
    tbody tr:last-child > * {
      border-bottom: 0;
    }
    tbody tr:hover > * {
      background: var(--surface-hover);
    }
    .positive {
      color: var(--positive);
    }
    .strong {
      color: var(--text-primary);
      font-weight: 600;
    }
    .muted {
      color: var(--text-muted);
    }
    tr.crossover > * {
      background: var(--accent-soft);
    }
  `,
})
export class ScheduleTable {
  readonly rows = input.required<YearRow[]>();
  readonly crossoverYear = input<number | null>(null);

  protected readonly i18n = inject(I18n);
}
