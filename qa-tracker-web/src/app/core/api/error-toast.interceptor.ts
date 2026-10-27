import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ToastService } from '../feedback/toast.service';
import { SILENT_ERRORS, problemMessage } from './api';

/**
 * Global toasts for errors a page cannot fix itself: offline, forbidden, rate-limited, server errors.
 * 400/404/409/422 are left to the caller (inline form errors, empty states). 401 is handled by authInterceptor.
 */
export const errorToastInterceptor: HttpInterceptorFn = (request, next) => {
  const toast = inject(ToastService);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && !request.context.get(SILENT_ERRORS)) {
        if (error.status === 0 || error.status >= 500) {
          toast.error(problemMessage(error));
        } else if (error.status === 403) {
          toast.error('You do not have permission to do that.');
        } else if (error.status === 429) {
          toast.warn(problemMessage(error, 'Too many requests. Wait a moment and try again.'));
        }
      }
      return throwError(() => error);
    }),
  );
};
