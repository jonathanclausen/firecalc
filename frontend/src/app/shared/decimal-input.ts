import { DestroyRef, Directive, ElementRef, afterNextRender, effect, inject } from '@angular/core';
import { I18n } from '../core/i18n/i18n';
import type { Lang } from '../core/i18n/translations';
import { formatDecimal, isSignificant, parseDecimal, regroupTyped } from './parse-decimal';

/**
 * Puts thousands separators in a text number field as the user types, in the UI language
 * (1.234.567,5 in Danish, 1,234,567.5 in English), keeping the caret by the same digit.
 * It runs before the field's own (input) handler, which reads the regrouped text and parses it
 * with parseDecimal; values set from code should be written with formatDecimal.
 */
@Directive({ selector: 'input[appDecimal]' })
export class DecimalInput {
  private readonly input = inject<ElementRef<HTMLInputElement>>(ElementRef).nativeElement;
  private readonly i18n = inject(I18n);

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      // Capturing runs on the field itself before the bubbling (input) bindings do.
      const listener = (event: Event) => this.regroup(event);
      this.input.addEventListener('input', listener, { capture: true });
      destroyRef.onDestroy(() =>
        this.input.removeEventListener('input', listener, { capture: true }),
      );
    });

    // Rewrites the number in the new language's marks and tells the page, so "1.234" stays 1234.
    let lang = this.i18n.lang();
    effect(() => {
      const next = this.i18n.lang();
      if (next === lang) return;
      const value = parseDecimal(this.input.value, lang);
      lang = next;
      if (value === null) return;
      this.input.value = formatDecimal(value, next);
      this.input.dispatchEvent(new Event('input', { bubbles: true }));
    });
  }

  private regroup(event: Event) {
    const lang = this.i18n.lang();
    const raw = this.input.value;
    // Typing and deleting treat the thousands mark as grouping only; pasted text may use either.
    const kind = (event as InputEvent).inputType ?? '';
    const typing = kind.startsWith('insertText') || kind.startsWith('delete');
    const next = regroupTyped(raw, lang, typing);
    if (next === null || next === raw) return;

    const caret = this.input.selectionStart ?? raw.length;
    const before =
      [...raw.slice(0, caret)].filter((c) => /[\d-]/.test(c)).length +
      (decimalBefore(raw, caret, lang, typing) ? 1 : 0);
    this.input.value = next;
    let position = 0;
    for (let seen = 0; position < next.length && seen < before; position++) {
      if (isSignificant(next[position], lang)) seen++;
    }
    if (this.input === this.input.ownerDocument.activeElement) {
      this.input.setSelectionRange(position, position);
    }
  }
}

/** Whether the number's decimal mark lies before the caret. */
function decimalBefore(raw: string, caret: number, lang: Lang, typing: boolean) {
  const decimal = formatDecimal(0.5, lang)[1];
  const hasDecimal = (text: string) => regroupTyped(text, lang, typing)?.includes(decimal) ?? false;
  return hasDecimal(raw) && hasDecimal(raw.slice(0, caret));
}
