import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { EMPTY, catchError, filter, startWith, switchMap } from 'rxjs';
import { problemMessage, problemOf } from '../../core/api/api';
import { applyServerErrors, fieldError } from '../../shared/forms/form-errors';
import { Cascade } from '../../shared/data/masters.api';
import { Sprint, SprintsApi } from '../../shared/data/sprints.api';

export interface StartSprintDialogData {
  readonly projectId: number;
  readonly projectName: string;
  readonly environments: Cascade['environments'];
  /** Pre-fill from the previous sprint (e.g. "Round 2" after a completed sprint). */
  readonly phaseId?: number | null;
  readonly defaultEnvironmentId?: number | null;
}

/** "+ Start new sprint" (plan 6.1): name (suggested), phase, default environment. Closes with the new sprint. */
@Component({
  selector: 'app-start-sprint-dialog',
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
    <h2 mat-dialog-title>
      Start a testing sprint
      <span class="block text-sm font-normal text-muted">in {{ data.projectName }}</span>
    </h2>
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
          <mat-label>Phase</mat-label>
          <mat-select formControlName="phaseId" cdkFocusInitial>
            @for (phase of phases.value() ?? []; track phase.id) {
              <mat-option [value]="phase.id">{{ phase.name }}</mat-option>
            }
          </mat-select>
          @if (phases.error()) {
            <mat-hint class="text-danger">Could not load phases.</mat-hint>
          }
          <mat-error>{{ fieldError(form.controls.phaseId, 'Phase') }}</mat-error>
        </mat-form-field>

        <mat-form-field appearance="outline" class="w-full">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" autocomplete="off" maxlength="100" />
          <mat-hint>Suggested from the phase – change it if you like.</mat-hint>
          <mat-error>{{ fieldError(form.controls.name, 'Name') }}</mat-error>
        </mat-form-field>

        <mat-form-field appearance="outline" class="w-full">
          <mat-label>Default environment</mat-label>
          <mat-select formControlName="defaultEnvironmentId">
            <mat-option [value]="null">None</mat-option>
            @for (env of data.environments; track env.id) {
              <mat-option [value]="env.id">{{ env.name }} · {{ env.url }}</mat-option>
            }
          </mat-select>
          <mat-hint>New issues are "found on" this environment unless you pick another.</mat-hint>
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>Cancel</button>
        <button matButton="filled" type="submit" [disabled]="saving()">
          {{ saving() ? 'Starting…' : 'Start sprint' }}
        </button>
      </mat-dialog-actions>
    </form>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StartSprintDialog {
  protected readonly data = inject<StartSprintDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<StartSprintDialog, Sprint>>(MatDialogRef);
  private readonly api = inject(SprintsApi);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly fieldError = fieldError;
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = this.fb.group({
    phaseId: this.fb.control<number | null>(this.data.phaseId ?? null, Validators.required),
    name: this.fb.control('', [Validators.required, Validators.maxLength(100)]),
    defaultEnvironmentId: this.fb.control<number | null>(
      this.data.defaultEnvironmentId ??
        (this.data.environments.length === 1 ? this.data.environments[0].id : null),
    ),
  });

  protected readonly phases = rxResource({ stream: () => this.api.phases() });

  constructor() {
    const { phaseId, name } = this.form.controls;
    // The name follows the phase until the user types their own.
    phaseId.valueChanges
      .pipe(
        startWith(phaseId.value),
        filter((id): id is number => id !== null && !name.dirty),
        switchMap((id) =>
          this.api.suggestedName(this.data.projectId, id).pipe(catchError(() => EMPTY)),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((suggestion) => name.setValue(suggestion.name));

    // Without a pre-filled phase, take the first one.
    effect(() => {
      const phases = this.phases.value();
      if (phases?.length && untracked(() => phaseId.value) === null) {
        phaseId.setValue(phases[0].id);
      }
    });
  }

  protected save(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.saving()) {
      return;
    }
    const value = this.form.getRawValue();
    this.saving.set(true);
    this.error.set(null);
    this.api
      .start(this.data.projectId, {
        name: value.name.trim(),
        phaseId: value.phaseId!,
        defaultEnvironmentId: value.defaultEnvironmentId,
      })
      .subscribe({
        next: (sprint) => this.ref.close(sprint),
        error: (error: unknown) => {
          this.saving.set(false);
          const unmatched = applyServerErrors(this.form, problemOf(error));
          const problem = problemOf(error);
          this.error.set(
            unmatched.length ? unmatched.join(' ') : problem?.errors ? null : problemMessage(error),
          );
        },
      });
  }
}
