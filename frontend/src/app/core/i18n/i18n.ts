import { isPlatformBrowser } from '@angular/common';
import {
  DOCUMENT,
  Injectable,
  PLATFORM_ID,
  REQUEST,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { Title } from '@angular/platform-browser';
import { DEFAULT_LANG, LANGUAGES, Lang, TRANSLATIONS } from './translations';

const COOKIE = 'firecalc.lang';
const ONE_YEAR = 60 * 60 * 24 * 365;

/**
 * Runtime language switching. The choice lives in a cookie (not localStorage) so the
 * server can render the visitor's language and hydration sees identical markup.
 */
@Injectable({ providedIn: 'root' })
export class I18n {
  private readonly document = inject(DOCUMENT);
  private readonly request = inject(REQUEST, { optional: true });
  private readonly title = inject(Title);
  private readonly platformId = inject(PLATFORM_ID);

  readonly lang = signal<Lang>(this.initialLang());
  readonly t = computed(() => TRANSLATIONS[this.lang()]);
  readonly locale = computed(() => LANGUAGES.find((l) => l.code === this.lang())!.locale);
  readonly languages = LANGUAGES;

  constructor() {
    effect(() => {
      this.document.documentElement.lang = this.lang();
      this.title.setTitle(this.t().pageTitle);
    });
  }

  set(lang: Lang) {
    if (!(lang in TRANSLATIONS) || lang === this.lang()) return;
    this.lang.set(lang);
    this.document.cookie = `${COOKIE}=${lang}; path=/; max-age=${ONE_YEAR}; samesite=lax`;
  }

  /** Formats a plain number in the UI language, e.g. 7,5 in Danish. */
  number(value: number, fractionDigits = 0) {
    return new Intl.NumberFormat(this.locale(), {
      minimumFractionDigits: 0,
      maximumFractionDigits: fractionDigits,
    }).format(value);
  }

  /** Formats a percentage given in percent units, e.g. 7 -> "7 %" in Danish. */
  percent(value: number, fractionDigits = 1) {
    return new Intl.NumberFormat(this.locale(), {
      style: 'percent',
      maximumFractionDigits: fractionDigits,
    }).format(value / 100);
  }

  private initialLang(): Lang {
    // On the server read the request header; the server DOM has no document.cookie.
    const cookies = this.request
      ? (this.request.headers.get('cookie') ?? '')
      : isPlatformBrowser(this.platformId)
        ? this.document.cookie
        : '';
    const match = cookies.match(/(?:^|;\s*)firecalc\.lang=(da|en)\b/);
    return (match?.[1] as Lang | undefined) ?? DEFAULT_LANG;
  }
}
