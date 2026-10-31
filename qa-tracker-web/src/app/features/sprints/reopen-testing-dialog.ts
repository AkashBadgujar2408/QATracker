import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { problemMessage } from '../../core/api/api';
import { Sprint, SprintsApi } from '../../shared/data/sprints.api';

export interface ReopenTestingDialogData {
  readonly sprint: Sprint;
}

/** Admin override (plan 6): a completed sprint goes back to Testing. The reason is required and logged. */
@Component({
  selector: 'app-reopen-testing-dialog',
  imports: [FormsModule, MatDialogModule, MatButton, MatFormField, MatLabel, MatHint, MatInput],
  template: `
    <h2 mat-dialog-title>
      Reopen testing
      <span class="block text-sm font-normal text-muted">{{ data.sprint.name }}</span>
    </h2>
    <mat-dialog-content>
      <p class="mt-0 mb-4 text-base text-text">
        Testers can add and edit issues in this sprint again; developers cannot act on its issues
        until testing is completed once more. Due dates are set again then.
      </p>
      <mat-form-field appearance="outline" class="w-full">
        <mat-label>Why is testing reopened?</mat-label>
        <textarea matInput rows="3" maxlength="1000" required [(ngModel)]="note"></textarea>
        <mat-hint>Shown in the sprint history.</mat-hint>
      </mat-form-field>
      @if (error()) {
        <p role="alert" class="m-0 mt-3 text-severity-critical">{{ error() }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" mat-dialog-close>Cancel</button>
      <button
        matButton="filled"
        type="button"
        [disabled]="!note.trim() || saving()"
        (click)="reopen()"
      >
        {{ saving() ? 'Reopening…' : 'Reopen testing' }}
      </button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReopenTestingDialog {
  protected readonly data = inject<ReopenTestingDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<ReopenTestingDialog, Sprint>>(MatDialogRef);
  private readonly api = inject(SprintsApi);

  protected note = '';
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected reopen(): void {
    const note = this.note.trim();
    if (!note || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.api.reopenTesting(this.data.sprint.id, this.data.sprint.rowVersion, note).subscribe({
      next: (sprint) => this.ref.close(sprint),
      error: (error: unknown) => {
        this.saving.set(false);
        this.error.set(problemMessage(error));
      },
    });
  }
}
