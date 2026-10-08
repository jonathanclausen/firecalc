import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import type { FirebaseError } from 'firebase/app';
import type { Auth as FirebaseAuth, AuthCredential, AuthProvider, User } from 'firebase/auth';
import { firstValueFrom } from 'rxjs';
import { Me } from '../api/planner-api';
import { I18n } from '../i18n/i18n';

export type AuthState =
  | 'loading'
  | 'unconfigured'
  | 'signedOut'
  | 'verifyEmail'
  | 'denied'
  | 'signedIn';

export type ProviderId = 'google.com' | 'facebook.com' | 'password';

/** Firebase's public web config, served by the Node server from its environment. */
interface FirebaseConfig {
  apiKey: string;
  authDomain: string;
  projectId: string;
}

type FirebaseAuthModule = typeof import('firebase/auth');

/**
 * Sign-in through Firebase Authentication: Google, Facebook, or email and password. Firebase keeps
 * one user per email, so the different logins of one person share an id, refreshes the ID token
 * itself and sends the verification and password-reset emails. The API checks the token and,
 * unless sign-up is open, the allow-list.
 */
@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  private readonly i18n = inject(I18n);

  readonly state = signal<AuthState>('loading');
  readonly user = signal<Me | null>(null);
  readonly signedIn = computed(() => this.state() === 'signedIn');
  /**
   * True from the moment Firebase has a user until the API has answered who it is. That answer
   * can take seconds after a cold start, and the sign-in page shows a loading state meanwhile.
   */
  readonly signingIn = signal(false);
  /** The Firebase user's email while it waits for verification. */
  readonly pendingEmail = signal<string | null>(null);
  /** Logins attached to the signed-in user. */
  readonly providers = signal<ProviderId[]>([]);
  /**
   * Set when a Google or Facebook login found an account with the same email made another way.
   * Firebase only joins them once the person proves the existing login, so we remember the new
   * credential and link it after their next successful sign-in.
   */
  readonly linkPending = signal<{ email: string; provider: ProviderId } | null>(null);

  private auth: FirebaseAuth | null = null;
  private fb: FirebaseAuthModule | null = null;
  private pendingCredential: AuthCredential | null = null;
  private started = false;
  private warmedUp = false;

  /** A fresh ID token for API calls, or null when signed out. Firebase refreshes it when needed. */
  async getIdToken(): Promise<string | null> {
    return (await this.auth?.currentUser?.getIdToken()) ?? null;
  }

  /** Runs once in the browser: reads the config, loads Firebase and follows its sign-in state. */
  async start() {
    if (this.started) return;
    this.started = true;

    const config = await firstValueFrom(
      this.http.get<{ firebase?: FirebaseConfig; authEmulator?: string }>('/app-config.json'),
    ).catch(() => ({ firebase: undefined, authEmulator: undefined }));
    if (!config.firebase?.apiKey) {
      this.state.set('unconfigured');
      return;
    }

    // Loaded on demand so the calculator pages and the server render never pay for it.
    const [{ initializeApp }, fb] = await Promise.all([
      import('firebase/app'),
      import('firebase/auth'),
    ]);
    this.fb = fb;
    const app = initializeApp(config.firebase);
    this.auth = fb.initializeAuth(app, {
      persistence: [fb.indexedDBLocalPersistence, fb.browserLocalPersistence],
      popupRedirectResolver: fb.browserPopupRedirectResolver,
    });
    if (config.authEmulator) {
      fb.connectAuthEmulator(this.auth, config.authEmulator, { disableWarnings: true });
    }
    fb.onAuthStateChanged(this.auth, (user) => void this.onUser(user));
  }

  signInWithGoogle() {
    return this.popup(new this.fb!.GoogleAuthProvider());
  }

  signInWithFacebook() {
    return this.popup(new this.fb!.FacebookAuthProvider());
  }

  async signInWithPassword(email: string, password: string) {
    await this.fb!.signInWithEmailAndPassword(this.auth!, email.trim(), password);
  }

  /** Creates a password login and sends the verification email. */
  async signUp(email: string, password: string) {
    const { user } = await this.fb!.createUserWithEmailAndPassword(
      this.auth!,
      email.trim(),
      password,
    );
    await this.sendVerification(user);
  }

  /** Firebase emails a link to its own page where a new password is chosen. */
  async resetPassword(email: string) {
    this.setLanguage();
    try {
      await this.fb!.sendPasswordResetEmail(this.auth!, email.trim());
    } catch (error) {
      // Say the same whether or not the email has an account, so the form can't be used to look
      // up who uses FireCalc. Firebase's email enumeration protection does this too.
      if ((error as FirebaseError).code !== 'auth/user-not-found') throw error;
    }
  }

  async resendVerification() {
    const user = this.auth?.currentUser;
    if (user) await this.sendVerification(user);
  }

  /** Called after the person clicked the link in the email. */
  async checkVerified() {
    const user = this.auth?.currentUser;
    if (!user) return;
    await user.reload();
    // The old token still says the email is unverified.
    await user.getIdToken(true);
    await this.onUser(user);
  }

  /** Attaches another login to the signed-in user. */
  async link(provider: 'google.com' | 'facebook.com') {
    const user = this.auth?.currentUser;
    if (!user) return;
    const p =
      provider === 'google.com'
        ? new this.fb!.GoogleAuthProvider()
        : new this.fb!.FacebookAuthProvider();
    await this.fb!.linkWithPopup(user, p);
    this.providers.set(providerIds(user));
  }

  async signOut() {
    this.cancelLink();
    if (this.auth) await this.fb!.signOut(this.auth);
    else this.state.set('unconfigured');
  }

  /** Deletes everything stored in FireCalc, then the Firebase login itself. */
  async deleteAccount() {
    await firstValueFrom(this.http.delete('/api/me'));
    const user = this.auth?.currentUser;
    try {
      if (user) await this.fb!.deleteUser(user);
    } catch {
      // Firebase wants a recent sign-in to delete the login. The data is gone either way, and a
      // later sign-in with it simply starts empty.
    }
    await this.signOut();
  }

  cancelLink() {
    this.pendingCredential = null;
    this.linkPending.set(null);
  }

  /** Called when the API rejects the token. */
  expired() {
    if (this.state() === 'signedIn') void this.signOut();
  }

  private async popup(provider: AuthProvider) {
    this.setLanguage();
    try {
      await this.fb!.signInWithPopup(this.auth!, provider);
    } catch (error) {
      const e = error as FirebaseError & { customData?: { email?: string } };
      if (e.code !== 'auth/account-exists-with-different-credential') throw error;
      const fromError =
        provider instanceof this.fb!.FacebookAuthProvider
          ? this.fb!.FacebookAuthProvider.credentialFromError(e)
          : this.fb!.GoogleAuthProvider.credentialFromError(e);
      this.pendingCredential = fromError;
      this.linkPending.set({
        email: e.customData?.email ?? '',
        provider: provider.providerId as ProviderId,
      });
    }
  }

  private async onUser(user: User | null) {
    if (!user) {
      this.signingIn.set(false);
      this.user.set(null);
      this.providers.set([]);
      this.pendingEmail.set(null);
      this.state.set('signedOut');
      this.warmUp();
      return;
    }

    // Leave the sign-in page at once, so nobody clicks again while the API wakes up.
    if (this.state() !== 'signedIn') {
      this.signingIn.set(true);
      this.state.set('loading');
    }
    try {
      await this.load(user);
    } finally {
      this.signingIn.set(false);
    }
  }

  private async load(user: User) {
    if (this.pendingCredential) {
      const credential = this.pendingCredential;
      this.cancelLink();
      try {
        await this.fb!.linkWithCredential(user, credential);
      } catch {
        // Already linked, or the credential expired; signing in still worked.
      }
    }
    this.providers.set(providerIds(user));

    if (!user.emailVerified) {
      this.user.set(null);
      this.pendingEmail.set(user.email);
      this.state.set('verifyEmail');
      return;
    }
    this.pendingEmail.set(null);
    await this.verify();
  }

  /**
   * Wakes the API and its database while the person picks a login, so the request after sign-in
   * doesn't wait for a cold start. Only the first sign-out of a page load needs it.
   */
  private warmUp() {
    if (this.warmedUp) return;
    this.warmedUp = true;
    this.http.get('/api/warmup').subscribe({ error: () => undefined });
  }

  /** Asks the API who we are; this is also where the allow-list answers. */
  private async verify() {
    try {
      const me = await firstValueFrom(this.http.get<Me>('/api/me'));
      this.user.set(me);
      this.state.set('signedIn');
    } catch (error) {
      this.user.set(null);
      this.state.set(
        error instanceof HttpErrorResponse && error.status === 403 ? 'denied' : 'signedOut',
      );
    }
  }

  private async sendVerification(user: User) {
    this.setLanguage();
    await this.fb!.sendEmailVerification(user);
  }

  /** Firebase writes its emails and pages in this language. */
  private setLanguage() {
    if (this.auth) this.auth.languageCode = this.i18n.lang();
  }
}

function providerIds(user: User): ProviderId[] {
  return user.providerData
    .map((p) => p.providerId)
    .filter((id): id is ProviderId => ['google.com', 'facebook.com', 'password'].includes(id));
}

/** Maps a Firebase error to a key of the sign-in error texts. */
export function authErrorKey(error: unknown): AuthErrorKey | null {
  const code = (error as { code?: string })?.code ?? '';
  switch (code) {
    case 'auth/popup-closed-by-user':
    case 'auth/cancelled-popup-request':
      return null;
    case 'auth/invalid-credential':
    case 'auth/wrong-password':
    case 'auth/user-not-found':
    case 'auth/invalid-login-credentials':
      return 'wrongPassword';
    case 'auth/invalid-email':
    case 'auth/missing-email':
      return 'invalidEmail';
    case 'auth/email-already-in-use':
      return 'emailInUse';
    case 'auth/weak-password':
    case 'auth/password-does-not-meet-requirements':
      return 'weakPassword';
    case 'auth/too-many-requests':
      return 'tooMany';
    case 'auth/popup-blocked':
      return 'popupBlocked';
    case 'auth/credential-already-in-use':
      return 'linkedElsewhere';
    case 'auth/requires-recent-login':
      return 'recentLogin';
    default:
      return 'generic';
  }
}

export type AuthErrorKey =
  | 'wrongPassword'
  | 'invalidEmail'
  | 'emailInUse'
  | 'weakPassword'
  | 'tooMany'
  | 'popupBlocked'
  | 'linkedElsewhere'
  | 'recentLogin'
  | 'generic';
