/**
 * UI strings. Danish is the primary language; English mirrors its shape.
 * Interpolated strings are functions so callers pass already formatted values.
 */

export type Lang = 'da' | 'en';

export const LANGUAGES: readonly { code: Lang; label: string; locale: string }[] = [
  { code: 'da', label: 'Dansk', locale: 'da-DK' },
  { code: 'en', label: 'English', locale: 'en-GB' },
];

export const DEFAULT_LANG: Lang = 'da';

const da = {
  pageTitle: 'Renters rente · FireCalc',
  nav: {
    main: 'Hovedmenu',
    compoundInterest: 'Renters rente',
    scenarios: 'Scenarier',
    tracking: 'Økonomi',
    soon: 'Snart',
    comingSoon: 'Kommer snart',
  },
  language: 'Sprog',
  currency: 'Valuta',
  currencies: {
    DKK: 'Danske kroner',
    EUR: 'Euro',
    SEK: 'Svenske kroner',
    NOK: 'Norske kroner',
    USD: 'Amerikanske dollar',
    GBP: 'Britiske pund',
  } as Record<string, string>,
  footer: 'Beregningerne forudsætter et konstant afkast og er skøn, ikke økonomisk rådgivning.',

  calculator: {
    eyebrow: 'Beregner',
    title: 'Renters rente',
    lede: 'Se, hvordan et startbeløb og faste indbetalinger vokser, når afkastet selv giver afkast.',
    inputsLabel: 'Beregnerens indstillinger',
    yourPlan: 'Din plan',
    reset: 'Nulstil',
    startingPoint: 'Udgangspunkt',
    initialAmount: 'Startbeløb',
    recurringDeposit: 'Fast indbetaling',
    amountPer: (period: string) => `Beløb pr. ${period}`,
    frequency: 'Hyppighed',
    depositAt: 'Indbetal ved',
    periodStart: 'Periodens start',
    periodEnd: 'Periodens slut',
    depositIncrease: 'Årlig stigning i indbetaling',
    depositChanges: 'Ændringer undervejs',
    depositChangesHint:
      'Skift beløbet fra et bestemt år, fx en lavere indbetaling fra år 6. Sæt 0 for at holde pause.',
    changeFromYear: 'Fra år',
    changeAmount: 'Nyt beløb',
    addChange: 'Tilføj ændring',
    removeChange: (year: number) => `Fjern ændringen fra år ${year}`,
    growth: 'Vækst',
    annualReturn: 'Forventet årligt afkast',
    compounding: 'Rentetilskrivning',
    horizon: 'Tidshorisont',
    yearsUnit: 'år',
    inflation: 'Forventet inflation',
    frequencies: {
      daily: 'Daglig',
      monthly: 'Månedlig',
      quarterly: 'Kvartalsvis',
      semiannually: 'Halvårlig',
      annually: 'Årlig',
    } as Record<string, string>,
    periods: {
      monthly: 'måned',
      quarterly: 'kvartal',
      semiannually: 'halvår',
      annually: 'år',
    } as Record<string, string>,

    summary: 'Resultat',
    balanceAfter: (years: number) => `Saldo efter ${years} år`,
    realValue: (amount: string, inflation: string) =>
      `${amount} i dagens penge ved ${inflation} inflation`,
    totalDeposited: 'Indbetalt i alt',
    interestEarned: 'Afkast i alt',
    multiple: 'Vækstfaktor',
    splitLabel: (deposits: string, interest: string) =>
      `Slutsaldoen består af ${deposits} indbetalinger og ${interest} afkast`,
    splitDeposits: (pct: string) => `${pct} indbetalinger`,
    splitInterest: (pct: string) => `${pct} afkast`,

    growthTitle: 'Udvikling over tid',
    insights: 'Indsigter',
    crossoverTitle: 'Skæringspunkt',
    crossover: (year: number) =>
      `I <strong>år ${year}</strong> giver dine penge mere i afkast, end du indbetaler. Derfra gør porteføljen det meste af arbejdet.`,
    crossoverNoDeposit:
      'Tilføj en fast indbetaling for at se, hvornår afkastet overhaler det, du sætter ind.',
    crossoverNotReached: (years: number) =>
      `Afkastet overhaler ikke dine årlige indbetalinger inden for ${years} år. Prøv en længere horisont eller et højere afkast.`,
    doublingTitle: 'Fordoblingstid',
    doubling: (rate: string, years: string) =>
      `Med ${rate} om året fordobles pengene cirka hvert <strong>${years} år</strong> uden nye indbetalinger.`,
    doublingNever: 'Med 0 % afkast fordobles pengene aldrig af sig selv.',

    scheduleTitle: 'År for år',
    showTable: 'Vis tabel',
    hideTable: 'Skjul tabel',
    scheduleHint: (years: number) =>
      `En fuld oversigt over indbetalinger, afkast og saldo for hvert af de ${years} år.`,
  },

  chart: {
    legend: 'Forklaring',
    deposits: 'Indbetalinger',
    interest: 'Afkast',
    realBalance: 'Saldo i dagens penge',
    realShort: 'Dagens penge',
    balance: 'Saldo',
    now: 'Nu',
    year: (year: number) => `År ${year}`,
    today: 'I dag',
    endOfYear: (year: number) => `Ultimo år ${year}`,
    ariaLabel: 'Porteføljens udvikling år for år. Brug piletasterne til at se de enkelte år.',
  },

  table: {
    year: 'År',
    deposits: 'Indbetalt',
    interest: 'Afkast',
    totalDeposits: 'Indbetalt i alt',
    totalInterest: 'Afkast i alt',
    balance: 'Saldo',
    real: 'Dagens penge',
  },
};

export type Translations = typeof da;

const en: Translations = {
  pageTitle: 'Compound interest · FireCalc',
  nav: {
    main: 'Main',
    compoundInterest: 'Compound interest',
    scenarios: 'Scenarios',
    tracking: 'Tracking',
    soon: 'Soon',
    comingSoon: 'Coming soon',
  },
  language: 'Language',
  currency: 'Currency',
  currencies: {
    DKK: 'Danish krone',
    EUR: 'Euro',
    SEK: 'Swedish krona',
    NOK: 'Norwegian krone',
    USD: 'US dollar',
    GBP: 'British pound',
  },
  footer: 'Projections assume a constant return and are estimates, not financial advice.',

  calculator: {
    eyebrow: 'Calculator',
    title: 'Compound interest',
    lede: 'See how a starting balance and steady deposits grow when returns earn returns of their own.',
    inputsLabel: 'Calculator inputs',
    yourPlan: 'Your plan',
    reset: 'Reset',
    startingPoint: 'Starting point',
    initialAmount: 'Initial investment',
    recurringDeposit: 'Recurring deposit',
    amountPer: (period) => `Amount per ${period}`,
    frequency: 'Frequency',
    depositAt: 'Deposit at',
    periodStart: 'Start of period',
    periodEnd: 'End of period',
    depositIncrease: 'Yearly deposit increase',
    depositChanges: 'Changes over time',
    depositChangesHint:
      'Change the amount from a given year, e.g. a lower deposit from year 6. Use 0 to pause.',
    changeFromYear: 'From year',
    changeAmount: 'New amount',
    addChange: 'Add change',
    removeChange: (year) => `Remove the change from year ${year}`,
    growth: 'Growth',
    annualReturn: 'Expected annual return',
    compounding: 'Compounding',
    horizon: 'Time horizon',
    yearsUnit: 'years',
    inflation: 'Expected inflation',
    frequencies: {
      daily: 'Daily',
      monthly: 'Monthly',
      quarterly: 'Quarterly',
      semiannually: 'Half-yearly',
      annually: 'Yearly',
    },
    periods: {
      monthly: 'month',
      quarterly: 'quarter',
      semiannually: 'half-year',
      annually: 'year',
    },

    summary: 'Summary',
    balanceAfter: (years) => `Balance after ${years} ${years === 1 ? 'year' : 'years'}`,
    realValue: (amount, inflation) => `${amount} in today's money at ${inflation} inflation`,
    totalDeposited: 'Total deposited',
    interestEarned: 'Interest earned',
    multiple: 'Money multiple',
    splitLabel: (deposits, interest) =>
      `Final balance is ${deposits} deposits and ${interest} interest`,
    splitDeposits: (pct) => `${pct} deposits`,
    splitInterest: (pct) => `${pct} interest`,

    growthTitle: 'Growth over time',
    insights: 'Insights',
    crossoverTitle: 'Crossover point',
    crossover: (year) =>
      `In <strong>year ${year}</strong> your money earns more in interest than you deposit. From there, the portfolio does most of the work.`,
    crossoverNoDeposit: 'Add a recurring deposit to see when interest overtakes what you put in.',
    crossoverNotReached: (years) =>
      `Interest doesn't overtake your yearly deposits within ${years} years. Try a longer horizon or a higher return.`,
    doublingTitle: 'Doubling time',
    doubling: (rate, years) =>
      `At ${rate} a year, money doubles roughly every <strong>${years} years</strong> without any new deposits.`,
    doublingNever: 'With a 0% return your money never doubles on its own.',

    scheduleTitle: 'Year by year',
    showTable: 'Show table',
    hideTable: 'Hide table',
    scheduleHint: (years) =>
      `A full breakdown of deposits, interest and balance for each of the ${years} years.`,
  },

  chart: {
    legend: 'Legend',
    deposits: 'Deposits',
    interest: 'Interest',
    realBalance: "Balance in today's money",
    realShort: "Today's money",
    balance: 'Balance',
    now: 'Now',
    year: (year) => `Yr ${year}`,
    today: 'Today',
    endOfYear: (year) => `End of year ${year}`,
    ariaLabel: 'Portfolio growth by year. Use arrow keys to inspect years.',
  },

  table: {
    year: 'Year',
    deposits: 'Deposits',
    interest: 'Interest',
    totalDeposits: 'Total deposited',
    totalInterest: 'Total interest',
    balance: 'Balance',
    real: "Today's money",
  },
};

export const TRANSLATIONS: Record<Lang, Translations> = { da, en };
