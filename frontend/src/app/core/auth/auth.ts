import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DOCUMENT, Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Me } from '../api/planner-api';
import { readStorage, writeStorage } from '../storage';

const TOKEN_KEY = 'firecalc.idToken';
const GIS_SRC = 'https://accounts.google.com/gsi/client';
/** Treat a token as expired a minute early so requests don't race its expiry. */
const EXPIRY_MARGIN_MS = 60_000;

export type AuthState = 'loading' | 'unconfigured' | 'signedOut' | 'denied' | 'signedIn';

/** The bits of Google Identity Services we use. */
interface GoogleId {
  initialize(config: {
    client_id: string;
    callback: (response: { credential: string }) => void;
    auto_select?: boolean;
    use_fedcm_for_prompt?: boolean;
  }): void;
  renderButton(parent: HTMLElement, options: Record<string, unknown>): void;
  prompt(): void;
  disableAutoSelect(): void;
}

declare global {
  interface Window {
    google?: { accounts: { id: GoogleId } };
  }
}

/**
 * Google sign-in. The browser gets a Google ID token from Google Identity Services and sends it
 * to the API, which checks it and the allow-list. The token is kept in localStorage until it
 * expires (about an hour); Google's auto sign-in then hands out a fresh one.
 */
@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);

  readonly state = signal<AuthState>('loading');
  readonly user = signal<Me | null>(null);
  readonly signedIn = computed(() => this.state() === 'signedIn');

  private token: string | null = null;
  private clientId = '';
  private gis: Promise<GoogleId | null> | null = null;
  private expiryTimer: ReturnType<typeof setTimeout> | undefined;
  private started = false;

  /** Current ID token, or null when there is none or it has expired. */
  get idToken(): string | null {
    return this.token && expiresAt(this.token) - EXPIRY_MARGIN_MS > Date.now() ? this.token : null;
  }

  /** Runs once in the browser: reads config, restores a saved session and loads Google. */
  async start() {
    if (this.started) return;
    this.started = true;

    const config = await firstValueFrom(
      this.http.get<{ googleClientId: string }>('/app-config.json'),
    ).catch(() => ({ googleClientId: '' }));
    this.clientId = config.googleClientId;

    const saved = readStorage(TOKEN_KEY);
    if (saved) {
      this.token = saved;
      if (this.idToken) {
        await this.verify();
        // A rejected account keeps the "no access" message; the button lets them switch.
        if (this.state() === 'signedIn' || this.state() === 'denied') return;
      }
    }

    if (!this.clientId) {
      this.state.set('unconfigured');
      return;
    }
    this.state.set('signedOut');
    (await this.loadGoogle())?.prompt();
  }

  /** Draws Google's "Sign in with Google" button into the element. */
  async renderButton(el: HTMLElement, locale: string) {
    const google = await this.loadGoogle();
    google?.renderButton(el, {
      theme: 'outline',
      size: 'large',
      shape: 'pill',
      text: 'signin_with',
      locale,
    });
  }

  signOut() {
    this.clear();
    this.state.set(this.clientId ? 'signedOut' : 'unconfigured');
    window.google?.accounts.id.disableAutoSelect();
  }

  /** Called when the API rejects the token: drop it and let Google sign in again. */
  expired() {
    if (this.state() !== 'signedIn') return;
    this.clear();
    this.state.set('signedOut');
    window.google?.accounts.id.prompt();
  }

  private async onCredential(credential: string) {
    this.token = credential;
    writeStorage(TOKEN_KEY, credential);
    await this.verify();
  }

  /** Asks the API who we are; this is also where the allow-list answers. */
  private async verify() {
    try {
      const me = await firstValueFrom(this.http.get<Me>('/api/me'));
      this.user.set(me);
      this.state.set('signedIn');
      this.scheduleExpiry();
    } catch (error) {
      const denied = error instanceof HttpErrorResponse && error.status === 403;
      this.clear();
      this.state.set(denied ? 'denied' : this.clientId ? 'signedOut' : 'unconfigured');
    }
  }

  private scheduleExpiry() {
    clearTimeout(this.expiryTimer);
    if (!this.token) return;
    const ms = expiresAt(this.token) - EXPIRY_MARGIN_MS - Date.now();
    this.expiryTimer = setTimeout(() => this.expired(), Math.max(0, ms));
  }

  private clear() {
    clearTimeout(this.expiryTimer);
    this.token = null;
    this.user.set(null);
    writeStorage(TOKEN_KEY, '');
  }

  private loadGoogle(): Promise<GoogleId | null> {
    if (!this.clientId) return Promise.resolve(null);
    this.gis ??= new Promise<GoogleId | null>((resolve) => {
      const ready = () => {
        const id = window.google?.accounts.id ?? null;
        id?.initialize({
          client_id: this.clientId,
          callback: ({ credential }) => void this.onCredential(credential),
          auto_select: true,
          use_fedcm_for_prompt: true,
        });
        resolve(id);
      };
      if (window.google?.accounts) return ready();
      const script = this.document.createElement('script');
      script.src = GIS_SRC;
      script.async = true;
      script.onload = ready;
      script.onerror = () => resolve(null);
      this.document.head.appendChild(script);
    });
    return this.gis;
  }
}

/** Reads the exp claim (seconds) of a JWT without verifying it; the API does the verifying. */
export function expiresAt(jwt: string): number {
  try {
    const payload = jwt.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const exp = JSON.parse(atob(payload)).exp;
    return typeof exp === 'number' ? exp * 1000 : 0;
  } catch {
    return 0;
  }
}
