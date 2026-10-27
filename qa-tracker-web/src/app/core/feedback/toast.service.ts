import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

export type ToastKind = 'success' | 'info' | 'warn' | 'error';

const DURATION_MS: Readonly<Record<ToastKind, number>> = {
  success: 3000,
  info: 4000,
  warn: 6000,
  error: 8000,
};

/** Short, non-blocking feedback (plan 10.3: success toasts last 3 s). */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly snackBar = inject(MatSnackBar);

  success(message: string): void {
    this.show('success', message);
  }

  info(message: string): void {
    this.show('info', message);
  }

  warn(message: string): void {
    this.show('warn', message);
  }

  error(message: string): void {
    this.show('error', message);
  }

  private show(kind: ToastKind, message: string): void {
    this.snackBar.open(message, kind === 'success' ? undefined : 'Dismiss', {
      duration: DURATION_MS[kind],
      panelClass: ['qa-toast', `qa-toast-${kind}`],
      horizontalPosition: 'end',
      verticalPosition: 'bottom',
      politeness: kind === 'error' ? 'assertive' : 'polite',
    });
  }
}
