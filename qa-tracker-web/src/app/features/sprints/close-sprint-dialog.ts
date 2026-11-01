import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert } from '@ng-icons/lucide';
import { problemMessage } from '../../core/api/api';
import {
  CarryForward,
  CarryForwardResult,
  OpenIssue,
  SPRINT_STATUS_LABELS,
  Sprint,
  SprintsApi,
} from '../../shared/data/sprints.api';
import { PluralPipe } from '../../shared/pipes/plural.pipe';
import { SeverityBadge } from '../../shared/ui/severity-badge/severity-badge';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';
import { StatusPill } from '../../shared/ui/status-pill/status-pill';

export interface CloseSprintDialogData {
  /** "close": close, carrying every open issue forward first. "carry": move the chosen ones, keep the sprint open. */
  readonly mode: 'close' | 'carry';
  readonly sprint: Sprint;
  /** The project's sprints – the open ones other than this are the targets. */
  readonly sprints: readonly Sprint[];
}

export type CloseSprintDialogResult =
  | {
      readonly kind: 'closed';
      readonly sprint: Sprint;
      readonly carried: number;
      readonly target: Sprint | null;
    }
  | { readonly kind: 'carried'; readonly result: CarryForwardResult }
  /** There is no open sprint to carry to – the page starts one and comes back. */
  | { readonly kind: 'start' };

/** Sprints that can take carried issues: same project, not closed, not this one – Testing first, then newest. */
export function carryTargets(sprint: Sprint, sprints: readonly Sprint[]): Sprint[] {
  return sprints
    .filter((s) => s.id !== sprint.id && s.projectId === sprint.projectId && s.status !== 'Closed')
    .sort(
      (a, b) =>
        Number(b.status === 'Testing') - Number(a.status === 'Testing') ||
        b.startedAt.localeCompare(a.startedAt),
    );
}

/**
 * Close sprint / carry forward (plan 6.1, ADR-039). Closing needs every issue Closed or Rejected – the open ones are
 * carried forward to another open sprint of the project in the same request. "Carry forward" moves only the chosen
 * issues and leaves the sprint open.
 */
@Component({
  selector: 'app-close-sprint-dialog',
  imports: [
    FormsModule,
    MatDialogModule,
    MatButton,
    MatCheckbox,
    MatFormField,
    MatLabel,
    MatHint,
    MatInput,
    MatSelect,
    MatOption,
    NgIcon,
    PluralPipe,
    SeverityBadge,
    SkeletonLines,
    StatusPill,
  ],
  viewProviders: [provideIcons({ lucideTriangleAlert })],
  template: `
    <h2 mat-dialog-title>
      {{ closing ? 'Close sprint' : 'Carry issues forward' }}
      <span class="block text-sm font-normal text-muted">{{ data.sprint.name }}</span>
    </h2>
    <mat-dialog-content>
      @if (issues.isLoading()) {
        <app-skeleton-lines [lines]="5" />
      } @else if (issues.error()) {
        <p role="alert" class="m-0 text-danger">
          Could not load the open issues. {{ loadError() }}
        </p>
      } @else if (issues.value(); as open) {
        @if (closing && open.length === 0) {
          <p class="mt-0 mb-4 text-base text-text">
            All {{ data.sprint.issueCount | plural: 'issue' }} are closed or rejected. Closing
            freezes the report as it is now and makes the sprint read-only – comments stay possible.
          </p>
        } @else if (open.length === 0) {
          <p class="m-0 text-base text-text">This sprint has no open issues to carry forward.</p>
        } @else {
          @if (closing) {
            <div class="qa-warn mb-4">
              <ng-icon name="lucideTriangleAlert" size="16" class="mt-0.5 shrink-0" />
              <span
                >{{ open.length | plural: 'issue is' : 'issues are' }} still open.
                {{ open.length === 1 ? 'It moves' : 'They move' }} to the sprint you choose and keep
                {{ open.length === 1 ? 'its key' : 'their keys' }}; this sprint's report shows
                {{ open.length === 1 ? 'it' : 'them' }} as
                {{ open.length === 1 ? 'it is' : 'they are' }} now.</span
              >
            </div>
          } @else {
            <p class="mt-0 mb-4 text-base text-text">
              The chosen issues move to another sprint and keep their keys. This sprint stays open.
            </p>
          }

          @if (targets.length === 0) {
            <div class="qa-warn mb-4">
              <ng-icon name="lucideTriangleAlert" size="16" class="mt-0.5 shrink-0" />
              <span
                >There is no open sprint in this project to carry them to. Start one first.</span
              >
            </div>
          } @else {
            <mat-form-field appearance="outline" class="w-full">
              <mat-label>Carry forward to</mat-label>
              <mat-select [(ngModel)]="targetId" required>
                @for (t of targets; track t.id) {
                  <mat-option [value]="t.id"
                    >{{ t.name }} · {{ statusLabels[t.status] }}</mat-option
                  >
                }
              </mat-select>
              <mat-hint>{{ targetHint() }}</mat-hint>
            </mat-form-field>
          }

          <div class="mt-2 mb-1 flex items-center gap-2">
            <h3 class="m-0 flex-1 text-sm font-semibold text-muted">
              {{ closing ? 'Open issues' : 'Issues to carry forward' }}
            </h3>
            @if (!closing) {
              <mat-checkbox
                [checked]="chosen().size === open.length"
                [indeterminate]="chosen().size > 0 && chosen().size < open.length"
                (change)="chooseAll($event.checked)"
                >All</mat-checkbox
              >
            }
          </div>
          <ul class="qa-issues m-0 list-none p-0" aria-label="Open issues">
            @for (issue of open; track issue.id) {
              <li class="flex items-center gap-3 py-1.5">
                @if (!closing) {
                  <mat-checkbox
                    [checked]="chosen().has(issue.id)"
                    (change)="toggle(issue.id, $event.checked)"
                    [aria-label]="'Carry forward ' + issue.key"
                  />
                }
                <span class="w-24 shrink-0 font-mono text-sm text-muted">{{ issue.key }}</span>
                <span class="min-w-0 flex-1 truncate" [title]="issue.title">{{ issue.title }}</span>
                <app-severity-badge [severity]="issue.severity" />
                <app-status-pill [status]="issue.status" />
              </li>
            }
          </ul>
        }

        <mat-form-field appearance="outline" class="mt-4 w-full" subscriptSizing="dynamic">
          <mat-label>Note (optional)</mat-label>
          <textarea matInput rows="2" maxlength="500" [(ngModel)]="note"></textarea>
        </mat-form-field>
      }

      @if (error()) {
        <p role="alert" class="m-0 mt-3 text-danger">{{ error() }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" mat-dialog-close>Cancel</button>
      @if (needsTarget() && targets.length === 0) {
        <button matButton="filled" type="button" (click)="startSprint()">Start new sprint</button>
      } @else {
        <button matButton="filled" type="button" [disabled]="!canSubmit()" (click)="submit()">
          {{ submitLabel() }}
        </button>
      }
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
    .qa-issues {
      max-height: 280px;
      overflow-y: auto;
      border-top: 1px solid var(--qa-border);
    }
    .qa-issues li + li {
      border-top: 1px solid var(--qa-border);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CloseSprintDialog {
  protected readonly data = inject<CloseSprintDialogData>(MAT_DIALOG_DATA);
  private readonly ref =
    inject<MatDialogRef<CloseSprintDialog, CloseSprintDialogResult>>(MatDialogRef);
  private readonly api = inject(SprintsApi);

  protected readonly closing = this.data.mode === 'close';
  protected readonly statusLabels = SPRINT_STATUS_LABELS;
  protected readonly targets = carryTargets(this.data.sprint, this.data.sprints);
  protected readonly target = signal<number | null>(this.targets[0]?.id ?? null);
  protected note = '';
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly chosen = signal<ReadonlySet<number>>(new Set());

  protected get targetId(): number | null {
    return this.target();
  }
  protected set targetId(id: number | null) {
    this.target.set(id);
  }

  protected readonly issues = rxResource({
    stream: () => this.api.openIssues(this.data.sprint.id),
  });
  protected readonly loadError = computed(() => problemMessage(this.issues.error()));

  /** The issues the request moves: all open ones when closing, the ticked ones otherwise. */
  private readonly moving = computed<readonly OpenIssue[]>(() => {
    const open = this.issues.value() ?? [];
    return this.closing ? open : open.filter((i) => this.chosen().has(i.id));
  });

  protected readonly needsTarget = computed(
    () => !this.closing || (this.issues.value()?.length ?? 0) > 0,
  );

  protected readonly targetHint = computed(() => {
    const t = this.targets.find((s) => s.id === this.target());
    return t?.status === 'Testing'
      ? 'Still in testing – the issues get new due dates when its testing completes.'
      : 'Testing completed – the issues keep their due dates.';
  });

  protected readonly canSubmit = computed(
    () =>
      this.issues.hasValue() &&
      !this.saving() &&
      (this.closing
        ? !this.needsTarget() || !!this.target()
        : this.moving().length > 0 && !!this.target()),
  );

  protected readonly submitLabel = computed(() => {
    const n = this.moving().length;
    if (this.saving()) {
      return this.closing ? 'Closing…' : 'Carrying forward…';
    }
    if (!this.closing) {
      return n === 1 ? 'Carry forward 1 issue' : `Carry forward ${n} issues`;
    }
    return n === 0 ? 'Close sprint' : `Carry forward ${n} and close`;
  });

  protected toggle(id: number, checked: boolean): void {
    this.chosen.update((set) => {
      const next = new Set(set);
      if (checked) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }

  protected chooseAll(checked: boolean): void {
    this.chosen.set(new Set(checked ? (this.issues.value() ?? []).map((i) => i.id) : []));
  }

  protected startSprint(): void {
    this.ref.close({ kind: 'start' });
  }

  protected submit(): void {
    if (!this.canSubmit()) {
      return;
    }
    const moving = this.moving();
    const targetId = this.target();
    const carry: CarryForward | null =
      moving.length > 0 && targetId
        ? {
            targetSprintId: targetId,
            issues: moving.map((i) => ({ id: i.id, rowVersion: i.rowVersion })),
          }
        : null;
    const target = this.targets.find((s) => s.id === targetId) ?? null;
    this.saving.set(true);
    this.error.set(null);
    const fail = (error: unknown) => {
      this.saving.set(false);
      this.error.set(problemMessage(error));
    };
    if (this.closing) {
      this.api.close(this.data.sprint.id, this.data.sprint.rowVersion, this.note, carry).subscribe({
        next: (sprint) =>
          this.ref.close({
            kind: 'closed',
            sprint,
            carried: moving.length,
            target: carry ? target : null,
          }),
        error: fail,
      });
    } else if (carry) {
      this.api.carryForward(this.data.sprint.id, carry, this.note).subscribe({
        next: (result) => this.ref.close({ kind: 'carried', result }),
        error: fail,
      });
    }
  }
}
