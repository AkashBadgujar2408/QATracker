import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MatError, MatFormField, MatLabel, MatSuffix } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatProgressSpinner } from '@angular/material/progress-spinner';
import { Router } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEye, lucideEyeOff } from '@ng-icons/lucide';
import { problemMessage } from '../../core/api/api';
import { AuthService } from '../../core/auth/auth.service';
import { fieldError } from '../../shared/forms/form-errors';
import { AuthCard } from './auth-card';

@Component({
  selector: 'app-login-page',
  imports: [
    ReactiveFormsModule,
    AuthCard,
    MatFormField,
    MatLabel,
    MatError,
    MatSuffix,
    MatInput,
    MatButton,
    MatIconButton,
    MatCheckbox,
    MatProgressSpinner,
    NgIcon,
  ],
  viewProviders: [provideIcons({ lucideEye, lucideEyeOff })],
  templateUrl: './login-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  /** From the query string (withComponentInputBinding): where to go after signing in. */
  readonly returnUrl = input<string>();

  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = inject(NonNullableFormBuilder).group({
    userName: ['', [Validators.required, Validators.maxLength(50)]],
    password: ['', [Validators.required, Validators.maxLength(100)]],
    rememberMe: [false],
  });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly showPassword = signal(false);
  protected readonly fieldError = fieldError;

  protected submit(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    const { userName, password, rememberMe } = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.auth.login(userName.trim(), password, rememberMe).subscribe({
      next: (user) => {
        const target = user.mustChangePassword
          ? '/change-password'
          : safeReturnUrl(this.returnUrl());
        void this.router.navigateByUrl(target);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(problemMessage(error, 'Sign-in failed. Please try again.'));
        this.form.controls.password.reset();
      },
    });
  }
}

/** Only in-app paths – never redirect to another site from a query parameter. */
export function safeReturnUrl(url: string | undefined): string {
  return url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/login')
    ? url
    : '/home';
}
