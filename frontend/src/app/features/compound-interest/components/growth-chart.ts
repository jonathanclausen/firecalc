import {
  Component,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
  DestroyRef,
} from '@angular/core';
import { CompoundInterestResult } from '../../../core/finance/compound-interest';
import { CurrencySettings } from '../../../core/settings/currency';
import { MoneyPipe } from '../../../shared/money.pipe';

interface Point {
  year: number;
  contributions: number;
  balance: number;
  real: number;
}

const HEIGHT = 320;
const PAD = { top: 16, right: 16, bottom: 32, left: 64 };

/** Stacked area chart of deposits vs. interest over time, with an inflation-adjusted line. */
@Component({
  selector: 'app-growth-chart',
  imports: [MoneyPipe],
  templateUrl: './growth-chart.html',
  styleUrl: './growth-chart.scss',
})
export class GrowthChart {
  readonly result = input.required<CompoundInterestResult>();
  readonly initialAmount = input.required<number>();
  readonly showReal = input(true);

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly currency = inject(CurrencySettings);
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

  protected readonly points = computed<Point[]>(() => {
    const initial = this.initialAmount();
    return [
      { year: 0, contributions: initial, balance: initial, real: initial },
      ...this.result().rows.map((r) => ({
        year: r.year,
        contributions: r.totalContributions,
        balance: r.endBalance,
        real: r.realEndBalance,
      })),
    ];
  });

  private readonly innerWidth = computed(() => this.width() - PAD.left - PAD.right);
  private readonly innerHeight = HEIGHT - PAD.top - PAD.bottom;

  protected readonly yTicks = computed(() => {
    const max = Math.max(1, ...this.points().map((p) => p.balance));
    const step = niceStep(max / 4);
    const ticks: number[] = [];
    for (let v = 0; v <= max + step * 0.001; v += step) ticks.push(v);
    if (ticks[ticks.length - 1] < max) ticks.push(ticks[ticks.length - 1] + step);
    return ticks;
  });

  private readonly yMax = computed(() => this.yTicks()[this.yTicks().length - 1]);
  private readonly xMax = computed(() => Math.max(1, this.points().length - 1));

  protected readonly xTicks = computed(() => {
    const n = this.xMax();
    const maxTicks = Math.max(2, Math.floor(this.innerWidth() / 64));
    const step = Math.max(1, niceStep(n / maxTicks, true));
    const ticks: number[] = [];
    for (let v = 0; v <= n; v += step) ticks.push(v);
    return ticks;
  });

  protected x(year: number) {
    return PAD.left + (year / this.xMax()) * this.innerWidth();
  }

  protected y(value: number) {
    return PAD.top + this.innerHeight - (value / this.yMax()) * this.innerHeight;
  }

  protected readonly contributionArea = computed(() => {
    const pts = this.points();
    const top = pts.map((p) => `${this.x(p.year)},${this.y(p.contributions)}`).join(' L');
    const base = this.y(0);
    return `M${this.x(0)},${base} L${top} L${this.x(pts[pts.length - 1].year)},${base} Z`;
  });

  protected readonly interestArea = computed(() => {
    const pts = this.points();
    const top = pts.map((p) => `${this.x(p.year)},${this.y(p.balance)}`).join(' L');
    const bottom = [...pts]
      .reverse()
      .map((p) => `${this.x(p.year)},${this.y(p.contributions)}`)
      .join(' L');
    return `M${top} L${bottom} Z`;
  });

  protected readonly contributionLine = computed(() => this.line((p) => p.contributions));
  protected readonly balanceLine = computed(() => this.line((p) => p.balance));
  protected readonly realLine = computed(() => this.line((p) => p.real));

  private line(pick: (p: Point) => number) {
    return (
      'M' +
      this.points()
        .map((p) => `${this.x(p.year)},${this.y(pick(p))}`)
        .join(' L')
    );
  }

  protected readonly hovered = computed(() => {
    const i = this.hoverIndex();
    return i === null ? null : (this.points()[i] ?? null);
  });

  protected readonly tooltipLeft = computed(() => {
    const p = this.hovered();
    if (!p) return 0;
    const px = this.x(p.year);
    // Flip the tooltip to the left of the crosshair on the right half.
    return px > this.width() / 2 ? px - 12 : px + 12;
  });

  protected readonly tooltipFlipped = computed(() => {
    const p = this.hovered();
    return !!p && this.x(p.year) > this.width() / 2;
  });

  protected formatAxis(value: number) {
    return this.currency.format(value, { compact: true });
  }

  protected onPointerMove(event: PointerEvent) {
    const svg = event.currentTarget as SVGElement;
    const rect = svg.getBoundingClientRect();
    const scale = this.width() / rect.width;
    const px = (event.clientX - rect.left) * scale;
    const year = Math.round(((px - PAD.left) / this.innerWidth()) * this.xMax());
    this.hoverIndex.set(Math.min(this.xMax(), Math.max(0, year)));
  }

  protected onKey(event: KeyboardEvent) {
    const current = this.hoverIndex() ?? 0;
    if (event.key === 'ArrowRight') this.hoverIndex.set(Math.min(this.xMax(), current + 1));
    else if (event.key === 'ArrowLeft') this.hoverIndex.set(Math.max(0, current - 1));
    else return;
    event.preventDefault();
  }
}

/** Rounds a raw step up to 1, 2, 2.5 or 5 times a power of ten (whole numbers if `integer`). */
function niceStep(raw: number, integer = false) {
  if (raw <= 0) return 1;
  const magnitude = Math.pow(10, Math.floor(Math.log10(raw)));
  const residual = raw / magnitude;
  const allowQuarter = !integer || magnitude >= 10;
  const nice =
    residual <= 1
      ? 1
      : residual <= 2
        ? 2
        : residual <= 2.5 && allowQuarter
          ? 2.5
          : residual <= 5
            ? 5
            : 10;
  return nice * magnitude;
}
