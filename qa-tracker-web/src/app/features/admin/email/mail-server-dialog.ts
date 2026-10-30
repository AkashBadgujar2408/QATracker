import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { problemMessage, problemOf } from '../../../core/api/api';
import { applyServerErrors, fieldError } from '../../../shared/forms/form-errors';
import { EmailAdminApi, MailServerSettings } from './email-admin.api';

/**
 * Edit the SMTP server and sender (ADR-038). The password field stays empty: leave it empty to keep the saved one,
 * type to replace it, or tick "Remove the password".
 */
@Component({
  selector: 'app-mail-server-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatFormField,
    MatLabel,
    MatError,
    MatHint,
    MatInput,
    MatButton,
    MatCheckbox,
    MatSlideToggle,
  ],
  template: `
    <h2 mat-dialog-title>Mail server</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content>
        @if (error()) {
          <div
            role="alert"
            class="mb-3 rounded-control border border-severity-critical/40 bg-severity-critical/10 px-3 py-2 text-sm text-text"
          >
            {{ error() }}
          </div>
        }
        <div class="grid grid-cols-[1fr_7rem] gap-x-3">
          <mat-form-field appearance="outline">
            <mat-label>SMTP server</mat-label>
            <input matInput formControlName="smtpHost" autocomplete="off" spellcheck="false" />
            <mat-error>{{ fieldError(form.controls.smtpHost, 'SMTP server') }}</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Port</mat-label>
            <input matInput type="number" formControlName="smtpPort" min="1" max="65535" />
            <mat-error>{{ fieldError(form.controls.smtpPort, 'Port') }}</mat-error>
          </mat-form-field>
        </div>
        <mat-slide-toggle formControlName="smtpEnableSsl" class="mb-4 block"
          >Encrypt the connection (TLS / STARTTLS)</mat-slide-toggle
        >

        <div class="grid gap-x-3 sm:grid-cols-2">
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Sign-in user</mat-label>
            <input matInput formControlName="smtpUserName" autocomplete="off" spellcheck="false" />
            <mat-hint>Leave empty if the server needs no sign-in.</mat-hint>
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Password</mat-label>
            <input
              matInput
              type="password"
              formControlName="smtpPassword"
              autocomplete="new-password"
              [placeholder]="settings.passwordSet ? 'Saved – leave empty to keep' : ''"
            />
            <mat-hint>{{
              settings.passwordSet ? 'A password is saved.' : 'No password saved.'
            }}</mat-hint>
          </mat-form-field>
        </div>
        @if (settings.passwordSet && form.controls.smtpUserName.value.trim()) {
          <mat-checkbox formControlName="removePassword" class="mt-1 block"
            >Remove the saved password</mat-checkbox
          >
        }

        <div class="mt-4 grid gap-x-3 sm:grid-cols-2">
          <mat-form-field appearance="outline">
            <mat-label>Sender address</mat-label>
            <input matInput type="email" formControlName="fromAddress" autocomplete="off" />
            <mat-error>{{ fieldError(form.controls.fromAddress, 'Sender address') }}</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Sender name</mat-label>
            <input matInput formControlName="fromName" autocomplete="off" />
            <mat-error>{{ fieldError(form.controls.fromName, 'Sender name') }}</mat-error>
          </mat-form-field>
        </div>
        <p class="m-0 text-sm text-muted">
          The next mail uses these settings. Send a test mail afterwards to check them.
        </p>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>Cancel</button>
        <button matButton="filled" type="submit" [disabled]="busy()">
          {{ busy() ? 'Saving…' : 'Save mail server' }}
        </button>
      </mat-dialog-actions>
    </form>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailServerDialog {
  private readonly api = inject(EmailAdminApi);
  private readonly dialogRef =
    inject<MatDialogRef<MailServerDialog, MailServerSettings>>(MatDialogRef);
  protected readonly settings = inject<MailServerSettings>(MAT_DIALOG_DATA);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldError = fieldError;

  protected readonly form = inject(NonNullableFormBuilder).group({
    smtpHost: [this.settings.smtpHost, [Validators.required, Validators.maxLength(255)]],
    smtpPort: [
      this.settings.smtpPort,
      [Validators.required, Validators.min(1), Validators.max(65535)],
    ],
    smtpEnableSsl: [this.settings.smtpEnableSsl],
    smtpUserName: [this.settings.smtpUserName ?? '', Validators.maxLength(256)],
    smtpPassword: ['', Validators.maxLength(256)],
    removePassword: [false],
    fromAddress: [
      this.settings.fromAddress,
      [Validators.required, Validators.email, Validators.maxLength(256)],
    ],
    fromName: [this.settings.fromName, [Validators.required, Validators.maxLength(100)]],
  });

  protected save(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    const userName = v.smtpUserName.trim();
    this.busy.set(true);
    this.error.set(null);
    this.api
      .saveMailServer({
        smtpHost: v.smtpHost.trim(),
        smtpPort: Number(v.smtpPort),
        smtpEnableSsl: v.smtpEnableSsl,
        smtpUserName: userName || null,
        smtpPassword: v.removePassword ? '' : v.smtpPassword || null,
        fromAddress: v.fromAddress.trim(),
        fromName: v.fromName.trim(),
        rowVersion: this.settings.rowVersion,
      })
      .subscribe({
        next: (saved) => this.dialogRef.close(saved),
        error: (error: unknown) => {
          this.busy.set(false);
          const problem = problemOf(error);
          const unmatched = applyServerErrors(this.form, problem);
          if (!problem?.errors || unmatched.length) {
            this.error.set(unmatched[0] ?? problemMessage(error));
          }
        },
      });
  }
}
