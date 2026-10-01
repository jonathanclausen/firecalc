import { Component, computed, input, model } from '@angular/core';

let nextId = 0;

/** Labelled numeric input with optional unit adornment and range slider. */
@Component({
  selector: 'app-number-field',
  template: `
    <label class="field" [for]="id">
      <span class="field__label">{{ label() }}</span>
      <span class="field__control">
        <input
          [id]="id"
          type="number"
          inputmode="decimal"
          [min]="min()"
          [max]="max()"
          [step]="step()"
          [value]="value()"
          (input)="onInput($any($event.target).value)"
          (blur)="$any($event.target).value = value()"
        />
        @if (unit()) {
          <span class="field__unit">{{ unit() }}</span>
        }
      </span>
    </label>
    @if (sliderMax() !== null) {
      <input
        class="slider"
        type="range"
        [attr.aria-label]="label()"
        [min]="min()"
        [max]="sliderMax()"
        [step]="sliderStep()"
        [value]="value()"
        [style.--fill]="fillPercent()"
        (input)="onInput($any($event.target).value)"
      />
    }
  `,
  styleUrl: './number-field.scss',
})
export class NumberField {
  readonly label = input.required<string>();
  readonly value = model.required<number>();
  readonly unit = input<string>('');
  readonly min = input<number>(0);
  readonly max = input<number | null>(null);
  readonly step = input<number>(1);
  /** Upper end of the slider; omit to hide the slider. Typed values may exceed it. */
  readonly sliderMax = input<number | null>(null);
  readonly sliderStep = input<number | null>(null);

  protected readonly id = `nf-${nextId++}`;

  protected readonly fillPercent = computed(() => {
    const max = this.sliderMax() ?? 1;
    const pct = ((this.value() - this.min()) / (max - this.min())) * 100;
    return `${Math.min(100, Math.max(0, pct))}%`;
  });

  protected onInput(raw: string) {
    if (raw === '') return;
    const parsed = Number(raw);
    if (!Number.isFinite(parsed)) return;
    let next = Math.max(this.min(), parsed);
    const max = this.max();
    if (max !== null) next = Math.min(max, next);
    this.value.set(next);
  }
}
