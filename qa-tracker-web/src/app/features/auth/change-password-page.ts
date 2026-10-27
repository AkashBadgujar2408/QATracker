import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { Router } from '@angular/router';
import { problemMessage, problemOf } from '../../core/api/api';
import { AuthService } from '../../core/auth/auth.service';
import { ToastService } from '../../core/feedback/toast.service';
import { applyServerErrors, fieldError, passwordPolicyError } from '../../shared/forms/form-errors';
import { AuthCard } from './auth-card';
import { safeReturnUrl } from './login-page';

function sameAsNew(control: AbstractControl): ValidationErrors | null {
  const parent = control.parent;
  return parent && control.value && control.value !== parent.get('newPassword')?.value
    ? { mismatch: 'The passwords do not match.' }
    : null;
}

@Component({
  selector: 'app-change-password-page',
  imports: [
    ReactiveFormsModule,
    AuthCard,
    MatFormField,
    MatLabel,
    MatError,
    MatHint,
    MatInput,
    MatButton,
  ],
  template: `
    <app-auth-card
      [heading]="forced() ? 'Choose a new password' : 'Change password'"
      [subheading]="
        forced()
          ? 'Your password was set by an administrator. Choose your own password to continue.'
          : 'Signed in as ' + (auth.user()?.userName ?? '')
      "
    >
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="flex flex-col gap-1">
        @if (error()) {
          <div
            role="alert"
            class="mb-3 rounded-control border border-severity-critical/40 bg-severity-critical/10 px-3 py-2 text-sm text-text"
          >
            {{ error() }}
          </div>
        }
        <mat-form-field appearance="outline">
          <mat-label>Current password</mat-label>
          <input
            matInput
            type="password"
            formControlName="currentPassword"
            autocomplete="current-password"
          />
          <mat-error>{{ fieldError(form.controls.currentPassword, 'Current password') }}</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>New password</mat-label>
          <input
            matInput
            type="password"
            formControlName="newPassword"
            autocomplete="new-password"
          />
          <mat-hint>At least 8 characters with a letter and a digit.</mat-hint>
          <mat-error>{{ fieldError(form.controls.newPassword, 'New password') }}</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline" class="mt-2">
          <mat-label>Confirm new password</mat-label>
          <input
            matInput
            type="password"
            formControlName="confirmPassword"
            autocomplete="new-password"
          />
          <mat-error>{{ fieldError(form.controls.confirmPassword, 'Confirmation') }}</mat-error>
        </mat-form-field>

        <div class="mt-2 flex items-center justify-between gap-2">
          <button matButton type="button" (click)="cancel()">
            {{ forced() ? 'Sign out' : 'Cancel' }}
          </button>
          <button matButton="filled" type="submit" [disabled]="busy()">Change password</button>
        </div>
      </form>
    </app-auth-card>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChangePasswordPage {
  readonly returnUrl = input<string>();

  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  protected readonly form = inject(NonNullableFormBuilder).group({
    currentPassword: ['', Validators.required],
    newPassword: ['', [Validators.required, passwordPolicyError]],
    confirmPassword: ['', [Validators.required, sameAsNew]],
  });
  protected readonly forced = computed(() => this.auth.mustChangePassword());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldError = fieldError;

  constructor() {
    this.form.controls.newPassword.valueChanges.subscribe(() =>
      this.form.controls.confirmPassword.updateValueAndValidity(),
    );
  }

  protected submit(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const { currentPassword, newPassword } = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.auth.changePassword(currentPassword, newPassword).subscribe({
      next: () => {
        this.toast.success('Password changed.');
        void this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
      },
      error: (error: unknown) => {
        this.busy.set(false);
        const unmatched = applyServerErrors(this.form, problemOf(error));
        if (!problemOf(error)?.errors || unmatched.length) {
          this.error.set(unmatched[0] ?? problemMessage(error));
        }
      },
    });
  }

  protected cancel(): void {
    if (this.forced()) {
      this.auth.logout();
    } else {
      void this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
    }
  }
}
