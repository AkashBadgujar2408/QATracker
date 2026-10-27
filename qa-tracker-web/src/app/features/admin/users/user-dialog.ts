import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { Observable, map } from 'rxjs';
import { problemMessage, problemOf } from '../../../core/api/api';
import { applyServerErrors, fieldError } from '../../../shared/forms/form-errors';
import { ROLES, Role } from '../../../shared/models/role';
import { User, UserWithPassword, UsersApi } from './users.api';

export interface UserDialogData {
  /** Edit this user; create a new one when absent. */
  readonly user?: User;
}

/** Result: the created user with its temporary password, or the updated user. */
export type UserDialogResult = { created: UserWithPassword } | { updated: User };

const USER_NAME_PATTERN = /^[A-Za-z0-9._-]{3,50}$/;

@Component({
  selector: 'app-user-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatFormField,
    MatLabel,
    MatError,
    MatHint,
    MatInput,
    MatSelect,
    MatOption,
    MatButton,
  ],
  template: `
    <h2 mat-dialog-title>{{ editing ? 'Edit user' : 'New user' }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="flex flex-col gap-1">
        @if (error()) {
          <div
            role="alert"
            class="mb-3 rounded-control border border-severity-critical/40 bg-severity-critical/10 px-3 py-2 text-sm text-text"
          >
            {{ error() }}
          </div>
        }
        <mat-form-field appearance="outline" class="w-full">
          <mat-label>User name</mat-label>
          <input matInput formControlName="userName" autocomplete="off" spellcheck="false" />
          <mat-hint>{{
            editing ? 'User names cannot be changed.' : 'Used to sign in. Letters, digits, . _ -'
          }}</mat-hint>
          <mat-error>{{ fieldError(form.controls.userName, 'User name') }}</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline" class="mt-2 w-full">
          <mat-label>Full name</mat-label>
          <input matInput formControlName="fullName" autocomplete="off" />
          <mat-error>{{ fieldError(form.controls.fullName, 'Full name') }}</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline" class="w-full">
          <mat-label>E-mail</mat-label>
          <input matInput type="email" formControlName="email" autocomplete="off" />
          <mat-hint>For notifications only.</mat-hint>
          <mat-error>{{ fieldError(form.controls.email, 'E-mail') }}</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline" class="mt-2 w-full">
          <mat-label>Role</mat-label>
          <mat-select formControlName="role">
            @for (role of roles; track role) {
              <mat-option [value]="role">{{ role }}</mat-option>
            }
          </mat-select>
          <mat-error>{{ fieldError(form.controls.role, 'Role') }}</mat-error>
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>Cancel</button>
        <button matButton="filled" type="submit" [disabled]="busy()">
          {{ editing ? 'Save' : 'Create user' }}
        </button>
      </mat-dialog-actions>
    </form>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserDialog {
  private readonly api = inject(UsersApi);
  private readonly dialogRef = inject<MatDialogRef<UserDialog, UserDialogResult>>(MatDialogRef);
  private readonly user = inject<UserDialogData>(MAT_DIALOG_DATA).user;

  protected readonly editing = this.user !== undefined;
  protected readonly roles = ROLES;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldError = fieldError;

  protected readonly form = inject(NonNullableFormBuilder).group({
    userName: [
      { value: this.user?.userName ?? '', disabled: this.editing },
      [Validators.required, Validators.pattern(USER_NAME_PATTERN)],
    ],
    fullName: [this.user?.fullName ?? '', [Validators.required, Validators.maxLength(100)]],
    email: [
      this.user?.email ?? '',
      [Validators.required, Validators.email, Validators.maxLength(256)],
    ],
    role: [this.user?.role ?? ('Developer' as Role), Validators.required],
  });

  protected save(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const request: Observable<UserDialogResult> = this.user
      ? this.api
          .update(this.user.id, {
            fullName: value.fullName.trim(),
            email: value.email.trim(),
            role: value.role,
            avatarColor: this.user.avatarColor,
            rowVersion: this.user.rowVersion,
          })
          .pipe(map((updated) => ({ updated })))
      : this.api
          .create({
            userName: value.userName.trim(),
            fullName: value.fullName.trim(),
            email: value.email.trim(),
            role: value.role,
          })
          .pipe(map((created) => ({ created })));

    this.busy.set(true);
    this.error.set(null);
    request.subscribe({
      next: (result) => this.dialogRef.close(result),
      error: (error: unknown) => {
        this.busy.set(false);
        const problem = problemOf(error);
        const unmatched = applyServerErrors(this.form, problem);
        if (problem?.code === 'username_taken') {
          this.form.controls.userName.setErrors({ server: problem.detail });
        } else if (problem?.code === 'email_taken') {
          this.form.controls.email.setErrors({ server: problem.detail });
        } else if (!problem?.errors || unmatched.length) {
          this.error.set(unmatched[0] ?? problemMessage(error));
        }
      },
    });
  }
}
