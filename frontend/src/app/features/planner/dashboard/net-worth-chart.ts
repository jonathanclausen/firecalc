import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { ACCOUNT_TYPES, AccountType, SeriesPoint } from '../../../core/api/planner-api';
import { I18n } from '../../../core/i18n/i18n';
import { CurrencySettings } from '../../../core/settings/currency';
import { niceStep } from '../../../shared/chart-scale';
import { MoneyPipe } from '../../../shared/money.pipe';

const HEIGHT = 300;
const PAD = { top: 16, right: 20, bottom: 32, left: 64 };
const DAY = 86_400_000;
/** Minimum horizontal room per x-axis date label. */
const LABEL_SPACING = 84;

interface Layer {
  type: AccountType;
  area: string;
  line: string;
}

/**
 * Stacked areas of net worth by account type, one point per date, on a time axis. Loans hang below
 * zero, with a line for net worth after them.
 */
@Component({
  selector: 'app-net-worth-chart',
  imports: [MoneyPipe],
  templateUrl: './net-worth-chart.html',
  styleUrl: './net-worth-chart.scss',
})
export class NetWorthChart {
  readonly series = input.required<SeriesPoint[]>();
  readonly currency = input.required<string>();
  /** Points after this date are a projection: drawn faded, behind a "today" line. */
  readonly futureFrom = input<string | null>(null);

  protected readonly i18n = inject(I18n);
  private readonly currencySettings = inject(CurrencySettings);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly width = signal(720);
  protected readonly height = HEIGHT;
  protected readonly pad = PAD;
  protected readonly hoverIndex = signal<number | null>(null);

  constructor() {
    afterNextRender(() => {
      if (typeof ResizeObserver === 'undefined') return;
      const el = this.host.nativeElement.querySelector('.chart__plot') as HTMLElement;
      const observer = new ResizeObserver(([entry]) =>
        this.width.set(Math.max(280, Math.floor(entry.contentRect.width))),
      );
      observer.observe(el);
      this.destroyRef.onDestroy(() => observer.disconnect());
    });
  }

  /** Account types that appear anywhere in the history, bottom of the stack first. */
  protected readonly types = computed(() =>
    ACCOUNT_TYPES.filter((t) => t !== 'loan' && this.series().some((p) => (p.byType[t] ?? 0) > 0)),
  );

  /** Whether any loan shows in the history; it is drawn below zero. */
  protected readonly hasDebt = computed(() => this.series().some((p) => (p.byType.loan ?? 0) < 0));

  /** Every type in the legend and tooltip, loans last. */
  protected readonly legendTypes = computed<AccountType[]>(() =>
    this.hasDebt() ? [...this.types(), 'loan'] : this.types(),
  );

  private readonly times = computed(() => this.series().map((p) => Date.parse(p.date)));

  /** Time domain; a single point gets a month either side so it sits in the middle. */
  private readonly domain = computed(() => {
    const ts = this.times();
    const min = Math.min(...ts);
    const max = Math.max(...ts);
    return min === max ? [min - 30 * DAY, max + 30 * DAY] : [min, max];
  });

  private readonly innerWidth = computed(() => this.width() - PAD.left - PAD.right);
  private readonly innerHeight = HEIGHT - PAD.top - PAD.bottom;

  protected readonly yTicks = computed(() => {
    const types = this.types();
    const series = this.series();
    const top = series.map((p) => types.reduce((sum, t) => sum + (p.byType[t] ?? 0), 0));
    const max = Math.max(1, ...top, ...series.map((p) => p.total));
    const min = Math.min(0, ...series.map((p) => Math.min(p.byType.loan ?? 0, p.total)));
    const step = niceStep((max - min) / 4);
    const ticks: number[] = [];
    for (let v = Math.floor(min / step) * step; v <= max + step * 0.001; v += step) ticks.push(v);
    if (ticks[ticks.length - 1] < max) ticks.push(ticks[ticks.length - 1] + step);
    return ticks;
  });

  private readonly yMin = computed(() => this.yTicks()[0]);
  private readonly yMax = computed(() => this.yTicks()[this.yTicks().length - 1]);

  protected xAt(index: number) {
    const [min, max] = this.domain();
    return PAD.left + ((this.times()[index] - min) / (max - min)) * this.innerWidth();
  }

  protected y(value: number) {
    const [min, max] = [this.yMin(), this.yMax()];
    return PAD.top + this.innerHeight - ((value - min) / (max - min)) * this.innerHeight;
  }

  /** Dates to label, spaced so they don't collide; always includes the latest. */
  protected readonly xTicks = computed(() => {
    const n = this.series().length;
    const picked: number[] = [];
    for (let i = n - 1; i >= 0; i--) {
      const last = picked[picked.length - 1];
      if (last === undefined || this.xAt(last) - this.xAt(i) >= LABEL_SPACING) picked.push(i);
    }
    return picked.reverse();
  });

  /** Running totals per type, so each layer sits on top of the ones below it. */
  private readonly stacks = computed(() => {
    const types = this.types();
    return this.series().map((p) => {
      let acc = 0;
      const tops = {} as Record<AccountType, { bottom: number; top: number }>;
      for (const t of types) {
        const bottom = acc;
        acc += p.byType[t] ?? 0;
        tops[t] = { bottom, top: acc };
      }
      return tops;
    });
  });

  protected readonly layers = computed<Layer[]>(() => {
    const stacks = this.stacks();
    if (stacks.length < 2) return [];
    return this.types().map((type) => {
      const top = stacks.map((s, i) => `${this.xAt(i)},${this.y(s[type].top)}`);
      const bottom = stacks.map((s, i) => `${this.xAt(i)},${this.y(s[type].bottom)}`).reverse();
      return {
        type,
        area: `M${top.join(' L')} L${bottom.join(' L')} Z`,
        line: `M${top.join(' L')}`,
      };
    });
  });

  /** Loans as an area from zero down, and net worth after them as a line. */
  protected readonly debt = computed(() => {
    const series = this.series();
    if (!this.hasDebt() || series.length < 2) return null;
    const low = series.map((p, i) => `${this.xAt(i)},${this.y(p.byType.loan ?? 0)}`);
    const zero = series.map((_, i) => `${this.xAt(i)},${this.y(0)}`).reverse();
    return {
      area: `M${low.join(' L')} L${zero.join(' L')} Z`,
      line: `M${low.join(' L')}`,
      net: `M${series.map((p, i) => `${this.xAt(i)},${this.y(p.total)}`).join(' L')}`,
    };
  });

  /** Where the projection starts on the x-axis, or null without one. */
  protected readonly futureX = computed(() => {
    const from = this.futureFrom();
    if (!from) return null;
    const [min, max] = this.domain();
    const t = Date.parse(from);
    return t > max ? null : PAD.left + ((t - min) / (max - min)) * this.innerWidth();
  });

  protected isFuture(date: string) {
    const from = this.futureFrom();
    return !!from && date > from;
  }

  protected readonly hovered = computed(() => {
    const i = this.hoverIndex();
    return i === null ? null : { index: i, point: this.series()[i] };
  });

  protected readonly tooltipFlipped = computed(() => {
    const h = this.hovered();
    return !!h && this.xAt(h.index) > this.width() / 2;
  });

  protected formatAxis(value: number) {
    return this.currencySettings.format(value, { compact: true, currency: this.currency() });
  }

  protected onPointerMove(event: PointerEvent) {
    const svg = event.currentTarget as SVGElement;
    const rect = svg.getBoundingClientRect();
    const px = (event.clientX - rect.left) * (this.width() / rect.width);
    let best = 0;
    for (let i = 1; i < this.series().length; i++) {
      if (Math.abs(this.xAt(i) - px) < Math.abs(this.xAt(best) - px)) best = i;
    }
    this.hoverIndex.set(best);
  }

  protected onKey(event: KeyboardEvent) {
    const last = this.series().length - 1;
    const current = this.hoverIndex() ?? last;
    if (event.key === 'ArrowRight') this.hoverIndex.set(Math.min(last, current + 1));
    else if (event.key === 'ArrowLeft') this.hoverIndex.set(Math.max(0, current - 1));
    else return;
    event.preventDefault();
  }
}
