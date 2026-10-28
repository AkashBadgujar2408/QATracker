import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert } from '@ng-icons/lucide';
import { problemMessage } from '../../core/api/api';
import { SEVERITIES } from '../../shared/models/issue-enums';
import { Sprint, SprintsApi } from '../../shared/data/sprints.api';
import { PluralPipe } from '../../shared/pipes/plural.pipe';
import { SeverityBadge } from '../../shared/ui/severity-badge/severity-badge';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';

export interface CompleteTestingDialogData {
  readonly sprint: Sprint;
}

/**
 * "Mark Testing Completed" (plan 6.1): counts by severity and module, unassigned issues and "Not assessed" severity
 * as warnings. Cannot be undone except by an admin. Closes with the updated sprint.
 */
@Component({
  selector: 'app-complete-testing-dialog',
  imports: [
    FormsModule,
    MatDialogModule,
    MatFormField,
    MatLabel,
    MatInput,
    MatButton,
    NgIcon,
    PluralPipe,
    SeverityBadge,
    SkeletonLines,
  ],
  viewProviders: [provideIcons({ lucideTriangleAlert })],
  template: `
    <h2 mat-dialog-title>
      Mark testing completed
      <span class="block text-sm font-normal text-muted">{{ data.sprint.name }}</span>
    </h2>
    <mat-dialog-content>
      @if (summary.isLoading()) {
        <app-skeleton-lines [lines]="5" />
      } @else if (summary.error()) {
        <p role="alert" class="m-0 text-severity-critical">
          Could not load the summary. {{ loadError() }}
        </p>
      } @else if (summary.value(); as s) {
        <p class="mt-0 mb-4 text-base text-text">
          Developers get {{ s.issueCount | plural: 'issue' }} to fix, with due dates from the SLA.
          You cannot add or edit issues in this sprint afterwards – only an admin can reopen
          testing.
        </p>

        @if (s.issueCount === 0) {
          <div class="qa-warn mb-4">
            <ng-icon name="lucideTriangleAlert" size="16" class="mt-0.5 shrink-0" />
            <span>This sprint has no issues.</span>
          </div>
        }
        @if (s.notAssessedCount > 0) {
          <div class="qa-warn mb-2">
            <ng-icon name="lucideTriangleAlert" size="16" class="mt-0.5 shrink-0" />
            <span
              >{{ s.notAssessedCount | plural: 'issue' }} still
              {{ s.notAssessedCount === 1 ? 'has' : 'have' }} severity “Not assessed” – they get no
              due date.</span
            >
          </div>
        }
        @if (s.unassignedCount > 0) {
          <div class="qa-warn mb-4">
            <ng-icon name="lucideTriangleAlert" size="16" class="mt-0.5 shrink-0" />
            <span
              >{{ s.unassignedCount | plural: 'issue' }} unassigned – developers will see
              {{ s.unassignedCount === 1 ? 'it' : 'them' }} under “Available to pick”.</span
            >
          </div>
        }

        @if (s.issueCount > 0) {
          <div class="grid gap-4 sm:grid-cols-2">
            <section>
              <h3 class="mt-0 mb-2 text-sm font-semibold text-muted">By severity</h3>
              <ul class="m-0 flex list-none flex-col gap-1.5 p-0">
                @for (row of severityRows(); track row.severity) {
                  <li class="flex items-center justify-between gap-3">
                    <app-severity-badge [severity]="row.severity" />
                    <span class="tabular-nums">{{ row.count }}</span>
                  </li>
                }
              </ul>
            </section>
            <section>
              <h3 class="mt-0 mb-2 text-sm font-semibold text-muted">By module</h3>
              <ul class="m-0 flex list-none flex-col gap-1.5 p-0">
                @for (row of s.byModule; track row.moduleId) {
                  <li class="flex items-center justify-between gap-3">
                    <span class="min-w-0 truncate">{{ row.moduleName }}</span>
                    <span class="tabular-nums">{{ row.count }}</span>
                  </li>
                }
              </ul>
            </section>
          </div>
        }

        <mat-form-field appearance="outline" class="mt-4 w-full" subscriptSizing="dynamic">
          <mat-label>Note (optional)</mat-label>
          <textarea matInput rows="2" maxlength="500" [(ngModel)]="note"></textarea>
        </mat-form-field>
      }

      @if (error()) {
        <p role="alert" class="m-0 mt-3 text-severity-critical">{{ error() }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" mat-dialog-close>Cancel</button>
      <button
        matButton="filled"
        type="button"
        class="qa-complete"
        [disabled]="!summary.hasValue() || saving()"
        (click)="complete()"
      >
        {{ saving() ? 'Completing…' : 'Mark testing completed' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .qa-warn {
      display: flex;
      gap: 0.5rem;
      align-items: flex-start;
      padding: 0.5rem 0.75rem;
      border-radius: var(--qa-radius-control);
      border: 1px solid color-mix(in srgb, var(--qa-severity-medium) 40%, transparent);
      background: color-mix(in srgb, var(--qa-severity-medium) 10%, transparent);
      color: var(--qa-text);
      font-size: var(--qa-text-sm);
    }
    .qa-warn ng-icon {
      color: var(--qa-severity-medium);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CompleteTestingDialog {
  protected readonly data = inject<CompleteTestingDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<CompleteTestingDialog, Sprint>>(MatDialogRef);
  private readonly api = inject(SprintsApi);

  protected note = '';
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly summary = rxResource({
    stream: () => this.api.completionSummary(this.data.sprint.id),
  });
  protected readonly loadError = computed(() => problemMessage(this.summary.error()));

  /** Severities in importance order, only those with issues. */
  protected readonly severityRows = computed(() => {
    const counts = this.summary.value()?.bySeverity ?? [];
    return SEVERITIES.map((severity) => ({
      severity,
      count: counts.find((c) => c.severity === severity)?.count ?? 0,
    })).filter((row) => row.count > 0);
  });

  protected complete(): void {
    if (this.saving()) {
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.api
      .completeTesting(this.data.sprint.id, this.data.sprint.rowVersion, this.note)
      .subscribe({
        next: (sprint) => this.ref.close(sprint),
        error: (error: unknown) => {
          this.saving.set(false);
          this.error.set(problemMessage(error));
        },
      });
  }
}
