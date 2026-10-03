import { Component, computed, inject, input, linkedSignal, model } from '@angular/core';
import { I18n } from '../core/i18n/i18n';
import { DecimalInput } from './decimal-input';
import { formatDecimal, parseDecimal } from './parse-decimal';

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
          type="text"
          inputmode="decimal"
          appDecimal
          [value]="text()"
          (input)="onInput($any($event.target).value)"
          (blur)="text.set(formatted())"
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
        (input)="onSlide($any($event.target).value)"
      />
    }
  `,
  styleUrl: './number-field.scss',
  imports: [DecimalInput],
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
  private readonly i18n = inject(I18n);

  protected readonly formatted = computed(() => formatDecimal(this.value(), this.i18n.lang()));
  /** What the field shows: the typed text while typing, the value with separators otherwise. */
  protected readonly text = linkedSignal(() => this.formatted());

  protected readonly fillPercent = computed(() => {
    const max = this.sliderMax() ?? 1;
    const pct = ((this.value() - this.min()) / (max - this.min())) * 100;
    return `${Math.min(100, Math.max(0, pct))}%`;
  });

  protected onInput(raw: string) {
    const parsed = parseDecimal(raw, this.i18n.lang());
    if (parsed !== null) this.setValue(parsed);
    this.text.set(raw);
  }

  protected onSlide(raw: string) {
    this.setValue(Number(raw));
  }

  private setValue(parsed: number) {
    let next = Math.max(this.min(), parsed);
    const max = this.max();
    if (max !== null) next = Math.min(max, next);
    this.value.set(next);
  }
}
