import { AbstractControl, FormGroup } from '@angular/forms';
import { ProblemDetails } from '../../core/api/api';

/**
 * The one place that turns validator errors into messages (plan 13.2).
 * Usage: <mat-error>{{ fieldError(form.controls.email, 'E-mail') }}</mat-error>
 */
export function fieldError(control: AbstractControl, label: string): string {
  const errors = control.errors;
  if (!errors) {
    return '';
  }
  if (errors['required']) {
    return `${label} is required.`;
  }
  if (errors['email']) {
    return 'Enter a valid e-mail address.';
  }
  if (errors['minlength']) {
    return `${label} must be at least ${errors['minlength'].requiredLength} characters.`;
  }
  if (errors['maxlength']) {
    return `${label} must be at most ${errors['maxlength'].requiredLength} characters.`;
  }
  if (errors['pattern']) {
    return `${label} has an invalid format.`;
  }
  if (errors['server']) {
    return errors['server'] as string;
  }
  const custom = Object.values(errors).find((value) => typeof value === 'string');
  return typeof custom === 'string' ? custom : `${label} is invalid.`;
}

/**
 * Puts API field errors (400 ProblemDetails `errors`, camelCase keys) on the matching controls.
 * Returns the messages that matched no control, for a form-level message.
 */
export function applyServerErrors(form: FormGroup, problem: ProblemDetails | null): string[] {
  const unmatched: string[] = [];
  for (const [field, messages] of Object.entries(problem?.errors ?? {})) {
    const control = form.get(field);
    if (control) {
      control.setErrors({ server: messages[0] });
      control.markAsTouched();
    } else {
      unmatched.push(...messages);
    }
  }
  return unmatched;
}

/** Password policy mirrored from the API (PasswordRules): 8–100 chars with a letter and a digit. */
export function passwordPolicyError(control: AbstractControl): Record<string, string> | null {
  const value = (control.value as string | null) ?? '';
  if (!value) {
    return null;
  }
  const ok = value.length >= 8 && value.length <= 100 && /\p{L}/u.test(value) && /\d/.test(value);
  return ok ? null : { policy: 'Use 8–100 characters with at least one letter and one digit.' };
}
