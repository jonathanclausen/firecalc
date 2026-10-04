import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import {
  AccountBalance,
  Home,
  HomeValuation,
  Me,
  Mortgage,
  PlannerApi,
  SaveMortgage,
  today,
} from '../../../core/api/planner-api';
import { I18n } from '../../../core/i18n/i18n';
import { DecimalInput } from '../../../shared/decimal-input';
import { MoneyPipe } from '../../../shared/money.pipe';
import { NgTemplateOutlet } from '@angular/common';
import { formatDecimal, parseDecimal } from '../../../shared/parse-decimal';

/** A loan's form: its terms and, when adding, what is owed on a date. */
interface LoanDraft {
  homeId: string;
  /** Null when adding a loan. */
  id: string | null;
  name: string;
  interestPct: string;
  contributionPct: string;
  endDate: string;
  interestOnlyUntil: string;
  owed: string;
  date: string;
}

/** A dated amount being entered: a home's value or a loan's restgæld. */
interface AmountDraft {
  kind: 'value' | 'statement';
  homeId: string;
  loanId: string | null;
  amount: string;
  date: string;
}

/**
 * Bolig: each home with what it is worth over time and the loans on it. A loan with a rate and an end date is
 * paid down month by month from the latest statement, so what is owed today needs no updating by hand.
 */
@Component({
  selector: 'app-home-page',
  imports: [MoneyPipe, DecimalInput, NgTemplateOutlet],
  templateUrl: './home-page.html',
  styleUrl: './home-page.scss',
})
export class HomePage {
  protected readonly i18n = inject(I18n);
  private readonly api = inject(PlannerApi);

  protected readonly homes = httpResource<Home[]>(() => '/api/homes?includeArchived=true');
  protected readonly showArchived = signal(false);
  protected readonly visible = computed(() =>
    (this.homes.value() ?? []).filter((h) => this.showArchived() || !h.archived),
  );
  protected readonly hasArchived = computed(() =>
    (this.homes.value() ?? []).some((h) => h.archived),
  );

  private readonly me = httpResource<Me>(() => '/api/me');
  protected readonly currency = computed(() => this.me.value()?.currency ?? 'DKK');
  protected readonly today = today();

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** Adding a home: its name, what it is worth and what is owed. */
  protected readonly addingHome = signal(false);
  protected readonly newHome = signal({ name: '', value: '', owed: '', date: today() });
  protected readonly renaming = signal<{ id: string; name: string } | null>(null);
  protected readonly amountDraft = signal<AmountDraft | null>(null);
  protected readonly loanDraft = signal<LoanDraft | null>(null);

  /** The value or statement history that is open: a home id, or a loan id. */
  protected readonly historyFor = signal<{ homeId: string; loanId: string | null } | null>(null);
  protected readonly history = httpResource<(HomeValuation | AccountBalance)[]>(() => {
    const h = this.historyFor();
    if (!h) return undefined;
    return h.loanId
      ? `/api/homes/${h.homeId}/loans/${h.loanId}/balances`
      : `/api/homes/${h.homeId}/values`;
  });

  protected setNewHome(field: 'name' | 'value' | 'owed' | 'date', value: string) {
    this.newHome.update((d) => ({ ...d, [field]: value }));
  }

  protected async addHome(event: Event) {
    event.preventDefault();
    const draft = this.newHome();
    const lang = this.i18n.lang();
    const value = parseDecimal(draft.value, lang);
    const owed = parseDecimal(draft.owed, lang);
    if (!draft.name.trim() || value === null || value < 0 || !draft.date) return;
    await this.run(async () => {
      const home = await this.api.createHome(draft.name.trim());
      await this.api.saveHomeValue(home.id, { date: draft.date, value });
      if (owed !== null && owed > 0) {
        const t = this.i18n.t().planner.homePage;
        const loan = await this.api.createMortgage(home.id, {
          ...this.noTerms(),
          name: t.defaultLoanName,
        });
        await this.api.saveMortgageBalance(home.id, loan.id, { date: draft.date, balance: owed });
      }
      this.newHome.set({ name: '', value: '', owed: '', date: this.today });
      this.addingHome.set(false);
    });
  }

  protected async saveRename(home: Home, event: Event) {
    event.preventDefault();
    const draft = this.renaming();
    if (!draft?.name.trim()) return;
    await this.run(async () => {
      await this.api.updateHome(home.id, { name: draft.name.trim(), archived: home.archived });
      this.renaming.set(null);
    });
  }

  protected setArchived(home: Home, archived: boolean) {
    return this.run(() => this.api.updateHome(home.id, { name: home.name, archived }));
  }

  protected async removeHome(home: Home) {
    const t = this.i18n.t().planner.homePage;
    if (!confirm(t.confirmDeleteHome(home.name))) return;
    await this.run(() => this.api.deleteHome(home.id));
  }

  // Values and statements

  protected startAmount(kind: AmountDraft['kind'], home: Home, loan: Mortgage | null = null) {
    this.loanDraft.set(null);
    const current = kind === 'value' ? home.value : loan?.owed;
    this.amountDraft.set({
      kind,
      homeId: home.id,
      loanId: loan?.id ?? null,
      amount:
        current === null || current === undefined ? '' : formatDecimal(current, this.i18n.lang()),
      date: this.today,
    });
  }

  protected setAmount(field: 'amount' | 'date', value: string) {
    this.amountDraft.update((d) => (d ? { ...d, [field]: value } : d));
  }

  protected isAmountFor(kind: AmountDraft['kind'], homeId: string, loanId: string | null = null) {
    const d = this.amountDraft();
    return !!d && d.kind === kind && d.homeId === homeId && d.loanId === loanId;
  }

  protected async saveAmount(event: Event) {
    event.preventDefault();
    const draft = this.amountDraft();
    const amount = parseDecimal(draft?.amount, this.i18n.lang());
    if (!draft || amount === null || amount < 0 || !draft.date) return;
    await this.run(async () => {
      if (draft.kind === 'value')
        await this.api.saveHomeValue(draft.homeId, { date: draft.date, value: amount });
      else
        await this.api.saveMortgageBalance(draft.homeId, draft.loanId!, {
          date: draft.date,
          balance: amount,
        });
      this.amountDraft.set(null);
      this.history.reload();
    });
  }

  protected toggleHistory(homeId: string, loanId: string | null = null) {
    const open = this.historyFor();
    this.historyFor.set(
      open?.homeId === homeId && open.loanId === loanId ? null : { homeId, loanId },
    );
  }

  protected isHistoryFor(homeId: string, loanId: string | null = null) {
    const open = this.historyFor();
    return open?.homeId === homeId && open.loanId === loanId;
  }

  protected amountOf(entry: HomeValuation | AccountBalance) {
    return 'value' in entry ? entry.value : entry.balance;
  }

  protected async removeEntry(entry: HomeValuation | AccountBalance) {
    const open = this.historyFor();
    const t = this.i18n.t().planner.homePage;
    if (!open || !confirm(t.confirmDeleteEntry(this.i18n.date(entry.date)))) return;
    await this.run(async () => {
      if (open.loanId) await this.api.deleteMortgageBalance(open.homeId, open.loanId, entry.date);
      else await this.api.deleteHomeValue(open.homeId, entry.date);
      this.history.reload();
    });
  }

  // Loans

  protected startLoan(home: Home, loan: Mortgage | null = null) {
    this.amountDraft.set(null);
    const lang = this.i18n.lang();
    const pct = (v: number | null | undefined) =>
      v === null || v === undefined ? '' : formatDecimal(v, lang);
    this.loanDraft.set({
      homeId: home.id,
      id: loan?.id ?? null,
      name: loan?.name ?? '',
      interestPct: pct(loan?.interestPct),
      contributionPct: pct(loan?.contributionPct),
      endDate: loan?.endDate ?? '',
      interestOnlyUntil: loan?.interestOnlyUntil ?? '',
      owed: '',
      date: this.today,
    });
  }

  protected setLoan(field: keyof LoanDraft, value: string) {
    this.loanDraft.update((d) => (d ? { ...d, [field]: value } : d));
  }

  protected isLoanFormFor(homeId: string, loanId: string | null) {
    const d = this.loanDraft();
    return !!d && d.homeId === homeId && d.id === loanId;
  }

  protected async saveLoan(event: Event, loan: Mortgage | null = null) {
    event.preventDefault();
    const draft = this.loanDraft();
    if (!draft || !draft.name.trim()) return;
    const lang = this.i18n.lang();
    const body: SaveMortgage = {
      name: draft.name.trim(),
      interestPct: parseDecimal(draft.interestPct, lang),
      contributionPct: parseDecimal(draft.contributionPct, lang),
      endDate: draft.endDate || null,
      interestOnlyUntil: draft.interestOnlyUntil || null,
      archived: loan?.archived ?? false,
    };
    const owed = parseDecimal(draft.owed, lang);
    await this.run(async () => {
      if (draft.id) await this.api.updateMortgage(draft.homeId, draft.id, body);
      else {
        const created = await this.api.createMortgage(draft.homeId, body);
        if (owed !== null && owed >= 0)
          await this.api.saveMortgageBalance(draft.homeId, created.id, {
            date: draft.date,
            balance: owed,
          });
      }
      this.loanDraft.set(null);
    });
  }

  protected setLoanArchived(home: Home, loan: Mortgage, archived: boolean) {
    return this.run(() =>
      this.api.updateMortgage(home.id, loan.id, {
        name: loan.name,
        interestPct: loan.interestPct,
        contributionPct: loan.contributionPct,
        endDate: loan.endDate,
        interestOnlyUntil: loan.interestOnlyUntil,
        archived,
      }),
    );
  }

  protected async removeLoan(home: Home, loan: Mortgage) {
    const t = this.i18n.t().planner.homePage;
    if (!confirm(t.confirmDeleteLoan(loan.name))) return;
    await this.run(() => this.api.deleteMortgage(home.id, loan.id));
  }

  protected paymentTotal(loan: Mortgage) {
    const p = loan.payment;
    return p ? p.interest + p.contribution + p.repayment : 0;
  }

  protected hasTerms(loan: Mortgage) {
    return loan.interestPct !== null && loan.endDate !== null;
  }

  protected percent(value: number | null) {
    return value === null ? '–' : this.i18n.percent(value, 2);
  }

  private noTerms() {
    return {
      interestPct: null,
      contributionPct: null,
      endDate: null,
      interestOnlyUntil: null,
      archived: false,
    };
  }

  /** Runs a write, reloads the homes and shows a message if it fails. */
  private async run(action: () => Promise<unknown>) {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      this.homes.reload();
    } catch {
      this.error.set(this.i18n.t().planner.error);
    } finally {
      this.busy.set(false);
    }
  }
}
