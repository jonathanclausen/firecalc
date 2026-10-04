import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { Auth } from './auth';

/** Sends the Firebase ID token with API calls and signs out when the API stops accepting it. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/')) return next(req);

  const auth = inject(Auth);
  return from(auth.getIdToken()).pipe(
    switchMap((token) =>
      next(token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req),
    ),
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) auth.expired();
      return throwError(() => error);
    }),
  );
};
