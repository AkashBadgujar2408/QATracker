import { HttpContextToken, HttpErrorResponse } from '@angular/common/http';
import { InjectionToken } from '@angular/core';

/** Base URL of the QA Tracker API. In development `/api` is proxied to the .NET API (proxy.conf.json). */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', { factory: () => '/api' });

/** Set on a request when the caller shows errors itself (no global toast). */
export const SILENT_ERRORS = new HttpContextToken<boolean>(() => false);

/** RFC 9457 problem returned by the API (plan 13.1) plus our `code` extension. */
export interface ProblemDetails {
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly code?: string;
  /** Field errors (camelCase field names) for 400 validation problems. */
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}

export function problemOf(error: unknown): ProblemDetails | null {
  if (error instanceof HttpErrorResponse && error.error && typeof error.error === 'object') {
    return error.error as ProblemDetails;
  }
  return null;
}

/** User-safe message for an HTTP error (ProblemDetails detail shown verbatim – plan 10.3). */
export function problemMessage(
  error: unknown,
  fallback = 'Something went wrong. Please try again.',
): string {
  if (error instanceof HttpErrorResponse && error.status === 0) {
    return 'Cannot reach the server. Check your connection and try again.';
  }
  const problem = problemOf(error);
  return problem?.detail ?? problem?.title ?? fallback;
}
