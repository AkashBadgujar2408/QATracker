import {
  HttpContextToken,
  HttpErrorResponse,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { API_BASE_URL } from '../api/api';
import { AuthService } from './auth.service';

const RETRIED = new HttpContextToken<boolean>(() => false);

/** Endpoints that work with the refresh cookie only (no bearer token, no retry). */
const COOKIE_ONLY = ['/auth/login', '/auth/refresh', '/auth/logout'];

/** Adds the bearer token to API calls; on 401 refreshes once and retries, otherwise ends the session. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const baseUrl = inject(API_BASE_URL);

  if (!request.url.startsWith(baseUrl) || COOKIE_ONLY.some((path) => request.url.endsWith(path))) {
    return next(request);
  }

  return next(withToken(request, auth.accessToken())).pipe(
    catchError((error: unknown) => {
      if (
        !(error instanceof HttpErrorResponse) ||
        error.status !== 401 ||
        request.context.get(RETRIED)
      ) {
        return throwError(() => error);
      }

      return auth.refresh().pipe(
        switchMap((refreshed) => {
          if (!refreshed) {
            auth.expireSession();
            return throwError(() => error);
          }
          const retry = withToken(request, auth.accessToken());
          return next(retry.clone({ context: retry.context.set(RETRIED, true) }));
        }),
      );
    }),
  );
};

function withToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
}
