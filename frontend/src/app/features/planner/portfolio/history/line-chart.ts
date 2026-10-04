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
import { I18n } from '../../../../core/i18n/i18n';
import { niceStep } from '../../../../shared/chart-scale';

const HEIGHT = 240;
const PAD = { top: 12, right: 16, bottom: 28, left: 60 };
const DAY = 86_400_000;
/** Minimum horizontal room per x-axis date label. */
const LABEL_SPACING = 84;

export interface LineSeries {
  /** Class suffix for the line colour, e.g. "value" → .line--value. */
  key: string;
  label: string;
  values: number[];
  dashed?: boolean;
}

/** Lines over a daily time axis with a crosshair; values share one y-axis. */
@Component({
  selector: 'app-line-chart',
  templateUrl: './line-chart.html',
  styleUrl: './line-chart.scss',
})
export class LineChart {
  readonly dates = input.required<string[]>();
  readonly series = input.required<LineSeries[]>();
  readonly format = input.required<(value: number, compact: boolean) => string>();
  readonly ariaLabel = input.required<string>();
  /** Draw a stronger line at zero (for returns that can go negative). */
  readonly zeroLine = input(false);
  /** Show the legend above the plot (when there is more than one line). */
  readonly legend = input(true);
  /** Labels a point on the x-axis (short) and in the tooltip; defaults to its date. */
  readonly xLabel = input<((index: number, short: boolean) => string) | null>(null);

  protected label(index: number, short: boolean) {
    const custom = this.xLabel();
    return custom
      ? custom(index, short)
      : this.i18n.date(this.dates()[index], short ? 'short' : 'long');
  }

  protected readonly i18n = inject(I18n);
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
        this.width.set(Math.max(260, Math.floor(entry.contentRect.width))),
      );
      observer.observe(el);
      this.destroyRef.onDestroy(() => observer.disconnect());
    });
  }

  private readonly times = computed(() => this.dates().map((d) => Date.parse(d)));

  private readonly domain = computed(() => {
    const ts = this.times();
    const min = ts[0] ?? 0;
    const max = ts[ts.length - 1] ?? 0;
    return min === max ? [min - DAY, max + DAY] : [min, max];
  });

  private readonly innerWidth = computed(() => this.width() - PAD.left - PAD.right);
  private readonly innerHeight = HEIGHT - PAD.top - PAD.bottom;

  /** Ticks cover the data with nice steps; the axis includes zero when values sit near it. */
  protected readonly yTicks = computed(() => {
    const all = this.series().flatMap((s) => s.values);
    let min = Math.min(0, ...all);
    let max = Math.max(0, ...all);
    // A value chart far above zero starts near its data instead of squashing it at the top.
    if (!this.zeroLine() && min >= 0 && all.length) {
      const low = Math.min(...all);
      if (low > max * 0.5) min = low;
    }
    if (max === min) max = min + 1;
    const step = niceStep((max - min) / 4);
    const first = Math.floor(min / step) * step;
    const ticks: number[] = [];
    for (let v = first; v < max + step - step * 0.001; v += step)
      ticks.push(Math.round(v * 1e6) / 1e6);
    if (ticks[ticks.length - 1] < max) ticks.push(ticks[ticks.length - 1] + step);
    return ticks;
  });

  protected xAt(index: number) {
    const [min, max] = this.domain();
    return PAD.left + ((this.times()[index] - min) / (max - min)) * this.innerWidth();
  }

  protected y(value: number) {
    const ticks = this.yTicks();
    const lo = ticks[0];
    const hi = ticks[ticks.length - 1];
    return PAD.top + this.innerHeight - ((value - lo) / (hi - lo)) * this.innerHeight;
  }

  protected readonly xTicks = computed(() => {
    const n = this.dates().length;
    const picked: number[] = [];
    for (let i = n - 1; i >= 0; i--) {
      const last = picked[picked.length - 1];
      if (last === undefined || this.xAt(last) - this.xAt(i) >= LABEL_SPACING) picked.push(i);
    }
    return picked.reverse();
  });

  protected readonly paths = computed(() =>
    this.series().map((s) => ({
      ...s,
      d: s.values.length
        ? 'M' +
          s.values.map((v, i) => `${this.xAt(i).toFixed(1)},${this.y(v).toFixed(1)}`).join(' L')
        : '',
    })),
  );

  protected readonly hovered = computed(() => {
    const i = this.hoverIndex();
    return i === null || i >= this.dates().length ? null : i;
  });

  /** On a phone the tooltip sits in the plot's far corner from the point, so it never runs off screen. */
  protected readonly narrow = computed(() => this.width() < 520);

  protected readonly tooltipFlipped = computed(() => {
    const i = this.hovered();
    return i !== null && this.xAt(i) > this.width() / 2;
  });

  protected onPointerMove(event: PointerEvent) {
    const svg = event.currentTarget as SVGElement;
    const rect = svg.getBoundingClientRect();
    const px = (event.clientX - rect.left) * (this.width() / rect.width);
    const [min, max] = this.domain();
    const t = min + ((px - PAD.left) / this.innerWidth()) * (max - min);
    // Dates are sorted, so a binary search finds the nearest point.
    const ts = this.times();
    let lo = 0;
    let hi = ts.length - 1;
    while (lo < hi) {
      const mid = (lo + hi) >> 1;
      if (ts[mid] < t) lo = mid + 1;
      else hi = mid;
    }
    const best = lo > 0 && Math.abs(ts[lo - 1] - t) < Math.abs(ts[lo] - t) ? lo - 1 : lo;
    this.hoverIndex.set(best);
  }

  /** A tap on a phone leaves the point shown; a mouse moving away hides it. */
  protected onPointerLeave(event: PointerEvent) {
    if (event.pointerType === 'mouse') this.hoverIndex.set(null);
  }

  protected onKey(event: KeyboardEvent) {
    const last = this.dates().length - 1;
    const current = this.hoverIndex() ?? last;
    if (event.key === 'ArrowRight') this.hoverIndex.set(Math.min(last, current + 1));
    else if (event.key === 'ArrowLeft') this.hoverIndex.set(Math.max(0, current - 1));
    else return;
    event.preventDefault();
  }
}
