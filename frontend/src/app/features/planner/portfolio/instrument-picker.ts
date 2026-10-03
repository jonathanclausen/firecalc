import { Component, computed, inject, input, output, signal } from '@angular/core';
import { InstrumentRef, PlannerApi, SymbolMatch } from '../../../core/api/planner-api';
import { I18n } from '../../../core/i18n/i18n';

/** A chosen share or fund, either one already owned or a search result. */
export interface InstrumentChoice extends InstrumentRef {
  name: string;
  label: string;
}

/** Search box for shares and funds by name, ticker or ISIN, with already-used ones offered first. */
@Component({
  selector: 'app-instrument-picker',
  templateUrl: './instrument-picker.html',
  styleUrl: './instrument-picker.scss',
})
export class InstrumentPicker {
  readonly known = input<InstrumentChoice[]>([]);
  readonly picked = output<InstrumentChoice>();

  protected readonly i18n = inject(I18n);
  private readonly api = inject(PlannerApi);

  protected readonly query = signal('');
  protected readonly matches = signal<SymbolMatch[] | null>(null);
  protected readonly searching = signal(false);
  /** Known instruments matching what has been typed so far. */
  protected readonly knownMatches = computed(() => {
    const q = this.query().trim().toLowerCase();
    return q ? this.known().filter((k) => k.label.toLowerCase().includes(q)) : this.known();
  });

  protected async search(event: Event) {
    event.preventDefault();
    const q = this.query().trim();
    if (!q) return;
    this.searching.set(true);
    try {
      this.matches.set(await this.api.searchInstruments(q));
    } catch {
      this.matches.set([]);
    } finally {
      this.searching.set(false);
    }
  }

  protected pickMatch(m: SymbolMatch) {
    const typed = this.query().trim().toUpperCase();
    const isin = /^[A-Z]{2}[A-Z0-9]{9}\d$/.test(typed) ? typed : null;
    this.picked.emit({ symbol: m.symbol, isin, name: m.name, label: `${m.name} · ${m.symbol}` });
    this.matches.set(null);
    this.query.set('');
  }
}
