import { httpResource } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { PortfolioHistory } from '../../../../core/api/planner-api';
import { I18n } from '../../../../core/i18n/i18n';

export type Range = 'm1' | 'm3' | 'ytd' | 'y1' | 'y5' | 'all' | 'custom';
export const RANGES: Exclude<Range, 'custom'>[] = ['m1', 'm3', 'ytd', 'y1', 'y5', 'all'];

export function iso(date: Date) {
  return date.toISOString().slice(0, 10);
}

/** Start date of a preset period ending today; null means since the first trade. */
export function rangeStart(range: Exclude<Range, 'custom'>, today: Date): string | null {
  const d = new Date(today);
  switch (range) {
    case 'm1':
      d.setMonth(d.getMonth() - 1);
      break;
    case 'm3':
      d.setMonth(d.getMonth() - 3);
      break;
    case 'ytd':
      return `${today.getFullYear()}-01-01`;
    case 'y1':
      d.setFullYear(d.getFullYear() - 1);
      break;
    case 'y5':
      d.setFullYear(d.getFullYear() - 5);
      break;
    case 'all':
      return null;
  }
  return iso(d);
}

/**
 * The period picked on the portfolio page and the combined history for it, shared by the tabs so
 * that picking "1 år" on one shows the same year on the others.
 */
@Injectable()
export class PortfolioPeriod {
  private readonly i18n = inject(I18n);

  readonly today = iso(new Date());
  readonly range = signal<Range>('y1');
  /** Dates typed into the custom period fields. */
  readonly customFrom = signal<string>(rangeStart('y1', new Date())!);
  readonly customTo = signal<string>(this.today);
  /** Bumped after a trade, so the history is fetched again. */
  readonly version = signal(0);

  readonly period = computed(() => {
    const range = this.range();
    if (range === 'custom') return { from: this.customFrom() || null, to: this.customTo() || null };
    return { from: rangeStart(range, new Date()), to: null };
  });

  readonly history = httpResource<PortfolioHistory>(() => {
    const { from, to } = this.period();
    this.version();
    const params: Record<string, string> = {};
    if (from) params['from'] = from;
    if (to) params['to'] = to;
    return { url: '/api/portfolio/history', params };
  });

  /** The period's name: the preset's label, or "i perioden" for picked dates. */
  readonly label = computed(() => {
    const h = this.i18n.t().planner.portfolioPage.history;
    const range = this.range();
    return range === 'custom' ? h.customPeriod : h.ranges[range];
  });

  /** Gain over the period: value change minus money put in. */
  readonly gain = computed(() => {
    const pts = this.history.value()?.points ?? [];
    if (pts.length === 0) return null;
    const first = pts[0];
    const last = pts[pts.length - 1];
    return last.value - last.netDeposits - (first.value - first.netDeposits);
  });

  pick(range: Range) {
    if (range === 'custom') {
      // Start the custom fields from the period on screen.
      const h = this.history.value();
      if (h) {
        this.customFrom.set(h.from);
        this.customTo.set(h.to);
      }
    }
    this.range.set(range);
  }

  setFrom(value: string) {
    this.customFrom.set(value);
    this.range.set('custom');
  }

  setTo(value: string) {
    this.customTo.set(value);
    this.range.set('custom');
  }

  refresh() {
    this.version.update((v) => v + 1);
  }
}

/** One security across the picked accounts over the period. */
export interface SecurityRow {
  instrumentId: string;
  name: string;
  symbol: string | null;
  quantity: number;
  value: number;
  gain: number;
  gainPct: number | null;
  dividends: number;
  sold: boolean;
  /** Names of the accounts it is (or was, in the period) held in. */
  accounts: string[];
}

/** Adds up each security across accounts (or one account), largest holding first, sold ones last. */
export function securityRows(history: PortfolioHistory, accountId: string | null): SecurityRow[] {
  const names = new Map(history.accounts.map((a) => [a.accountId, a.name]));
  const groups = new Map<string, SecurityRow & { invested: number }>();
  for (const s of history.securities) {
    if (accountId && s.accountId !== accountId) continue;
    const g = groups.get(s.instrumentId) ?? {
      instrumentId: s.instrumentId,
      name: s.name,
      symbol: s.symbol,
      quantity: 0,
      value: 0,
      gain: 0,
      gainPct: null,
      dividends: 0,
      sold: false,
      accounts: [],
      invested: 0,
    };
    g.quantity += s.quantity;
    g.value += s.value;
    g.gain += s.gain;
    g.invested += s.invested;
    g.dividends += s.dividends ?? 0;
    const name = names.get(s.accountId);
    if (name && (s.quantity > 0 || s.gain !== 0) && !g.accounts.includes(name))
      g.accounts.push(name);
    groups.set(s.instrumentId, g);
  }
  const rows: SecurityRow[] = [];
  for (const { invested, ...g } of groups.values()) {
    g.sold = g.quantity <= 0;
    // Sold before the period and untouched since: nothing to show.
    if (g.sold && Math.abs(g.gain) < 0.5) continue;
    g.gainPct = invested > 0 ? (g.gain / invested) * 100 : null;
    rows.push(g);
  }
  return rows.sort((a, b) =>
    a.sold !== b.sold ? (a.sold ? 1 : -1) : a.sold ? b.gain - a.gain : b.value - a.value,
  );
}

/** The picked accounts' totals; what the securities don't explain is cash, interest and fees. */
export function periodTotals(
  history: PortfolioHistory,
  accountId: string | null,
  rows: SecurityRow[],
) {
  const list = history.accounts.filter((a) => !accountId || a.accountId === accountId);
  const value = list.reduce((s, a) => s + a.value, 0);
  const gain = list.reduce((s, a) => s + a.gain, 0);
  const otherValue = value - rows.reduce((s, r) => s + r.value, 0);
  const otherGain = gain - rows.reduce((s, r) => s + r.gain, 0);
  const other =
    Math.abs(otherValue) >= 1 || Math.abs(otherGain) >= 1
      ? { value: otherValue, gain: otherGain }
      : null;
  return { value, gain, other };
}

const LOGO_COLORS = [
  '#2f5bd3',
  '#7c3aed',
  '#0f766e',
  '#0b5394',
  '#9a3412',
  '#334155',
  '#0e7490',
  '#a21caf',
];

/** A steady colour and up to three letters for a security's round icon. */
export function logo(name: string, symbol: string | null) {
  let hash = 0;
  for (const ch of name) hash = (hash * 31 + ch.charCodeAt(0)) | 0;
  const base = (symbol ?? '').split(/[.\-:]/)[0];
  const letters =
    base && base.length <= 4 && /^[A-Z0-9]+$/i.test(base)
      ? base.slice(0, 3)
      : name
          .split(/\s+/)
          .filter((w) => /^[\p{L}\d]/u.test(w))
          .slice(0, 2)
          .map((w) => w[0])
          .join('');
  return {
    color: LOGO_COLORS[Math.abs(hash) % LOGO_COLORS.length],
    letters: letters.toUpperCase(),
  };
}
