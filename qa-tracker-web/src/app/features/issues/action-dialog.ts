import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { Observable, concatMap, map, of } from 'rxjs';
import { problemMessage, problemOf } from '../../core/api/api';
import {
  AllowedAction,
  Deployment,
  Issue,
  IssueAction,
  IssuesApi,
  REJECT_TYPE_LABELS,
  RejectType,
} from '../../shared/data/issues.api';
import { Cascade, UserLookup } from '../../shared/data/masters.api';
import { TimelineApi } from '../../shared/data/timeline.api';
import { PasteDropzone } from '../../shared/ui/paste-dropzone/paste-dropzone';

export interface ActionDialogData {
  readonly issue: Issue;
  /** A workflow action, or a failed retest of one deployment. */
  readonly action:
    AllowedAction | { readonly action: 'RetestFailed'; readonly deployment: Deployment };
  readonly assignees: readonly UserLookup[];
  readonly environments: Cascade['environments'];
}

interface Copy {
  readonly title: string;
  readonly confirm: string;
  readonly reasonLabel?: string;
  readonly reasonHint?: string;
}

const COPY: Partial<Record<IssueAction | 'RetestFailed', Copy>> = {
  Assign: { title: 'Assign', confirm: 'Assign', reasonLabel: 'Note (optional)' },
  NeedInfo: {
    title: 'Ask for information',
    confirm: 'Ask',
    reasonLabel: 'Question for the reporter',
    reasonHint: 'Posted as a comment that mentions the reporter.',
  },
  Hold: { title: 'Put on hold', confirm: 'Put on hold', reasonLabel: 'Why is it on hold?' },
  Dispute: {
    title: 'Not a bug',
    confirm: 'Dispute',
    reasonLabel: 'Why is this not a bug?',
    reasonHint: 'The tester decides: reject it, or send it back to you.',
  },
  Reject: { title: 'Reject the issue', confirm: 'Reject', reasonLabel: 'Comment' },
  Contest: {
    title: 'Still a bug',
    confirm: 'Send back',
    reasonLabel: 'Why is it still a bug?',
    reasonHint: 'The issue goes back to the developer as Assigned.',
  },
  MarkFixed: { title: 'Mark fixed', confirm: 'Mark fixed', reasonLabel: 'What was fixed?' },
  Reopen: {
    title: 'Reopen',
    confirm: 'Reopen',
    reasonLabel: 'What still fails?',
    reasonHint: 'A screenshot helps the developer – paste it below.',
  },
  RetestFailed: {
    title: 'Retest failed',
    confirm: 'Reopen issue',
    reasonLabel: 'What still fails?',
    reasonHint: 'The issue is reopened. A screenshot helps – paste it below.',
  },
};

/**
 * Asks for what a workflow action needs (plan S15): assignee, reason, reject type (+ duplicate key), environments for a
 * fix, screenshots when reopening. Runs the action and closes with the updated issue.
 */
@Component({
  selector: 'app-action-dialog',
  imports: [
    FormsModule,
    MatDialogModule,
    MatButton,
    MatCheckbox,
    MatFormField,
    MatHint,
    MatLabel,
    MatInput,
    MatSelect,
    MatOption,
    PasteDropzone,
  ],
  template: `
    <h2 mat-dialog-title>
      {{ title() }}
      <span class="block text-sm font-normal text-muted"
        >{{ data.issue.key }} · {{ data.issue.title }}</span
      >
    </h2>
    <mat-dialog-content>
      <div class="flex flex-col gap-3 pt-1">
        @if (kind === 'RetestFailed') {
          <p class="m-0 text-sm text-muted">
            Retest on <strong class="text-text">{{ deploymentName() }}</strong>
          </p>
        }

        @if (kind === 'Assign') {
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Assign to</mat-label>
            <mat-select [(ngModel)]="assigneeId" cdkFocusInitial>
              @for (user of data.assignees; track user.id) {
                <mat-option [value]="user.id" [disabled]="user.id === data.issue.assigneeId">
                  {{ user.fullName }}
                  <span class="text-xs text-muted">· {{ user.role }}</span>
                </mat-option>
              }
            </mat-select>
          </mat-form-field>
        }

        @if (kind === 'Reject') {
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Reject as</mat-label>
            <mat-select [(ngModel)]="rejectType" cdkFocusInitial>
              @for (type of rejectTypes; track type) {
                <mat-option [value]="type">{{ rejectLabels[type] }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          @if (rejectType === 'Duplicate') {
            <mat-form-field appearance="outline" subscriptSizing="dynamic">
              <mat-label>Duplicate of</mat-label>
              <input
                matInput
                [(ngModel)]="duplicateOfKey"
                placeholder="{{ data.issue.projectKey }}-12"
                autocomplete="off"
              />
            </mat-form-field>
          }
        }

        @if (kind === 'MarkFixed') {
          <fieldset class="m-0 border-0 p-0">
            <legend class="mb-1 p-0 text-sm font-medium text-muted">Deployed on</legend>
            @for (env of data.environments; track env.id) {
              <mat-checkbox
                class="block"
                [checked]="environmentIds().has(env.id)"
                (change)="toggleEnvironment(env.id, $event.checked)"
              >
                {{ env.name }} <span class="text-xs text-muted">· {{ env.url }}</span>
              </mat-checkbox>
            } @empty {
              <p class="m-0 text-sm text-danger">
                The project has no environments – add one in Masters first.
              </p>
            }
          </fieldset>
        }

        @if (copy().reasonLabel) {
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>{{ copy().reasonLabel }}</mat-label>
            <textarea
              matInput
              rows="3"
              maxlength="2000"
              [(ngModel)]="reason"
              (keydown.control.enter)="run()"
            ></textarea>
            @if (copy().reasonHint) {
              <mat-hint>{{ copy().reasonHint }}</mat-hint>
            }
          </mat-form-field>
        }

        @if (kind === 'MarkFixed') {
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Version or commit (optional)</mat-label>
            <input matInput [(ngModel)]="version" maxlength="100" autocomplete="off" />
          </mat-form-field>
        }

        @if (kind === 'Reopen' || kind === 'RetestFailed') {
          <app-paste-dropzone
            [(files)]="files"
            capturePaste
            hint="Paste a screenshot (Ctrl+V) or drop files"
          />
        }

        @if (error()) {
          <p role="alert" class="m-0 text-sm text-danger">{{ error() }}</p>
        }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" mat-dialog-close>Cancel</button>
      <button matButton="filled" type="button" [disabled]="busy()" (click)="run()">
        {{ busy() ? 'Saving…' : copy().confirm }}
      </button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActionDialog {
  protected readonly data = inject<ActionDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<ActionDialog, Issue>>(MatDialogRef);
  private readonly issues = inject(IssuesApi);
  private readonly timeline = inject(TimelineApi);

  protected readonly kind = this.data.action.action;
  protected readonly rejectTypes = Object.keys(REJECT_TYPE_LABELS) as RejectType[];
  protected readonly rejectLabels = REJECT_TYPE_LABELS;

  protected assigneeId: number | null = null;
  protected reason = '';
  protected rejectType: RejectType | null = null;
  protected duplicateOfKey = '';
  protected version = '';
  protected readonly files = signal<readonly File[]>([]);
  protected readonly environmentIds = signal<ReadonlySet<number>>(
    new Set(this.defaultEnvironments()),
  );
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly copy = computed(
    () => COPY[this.kind] ?? { title: this.kind, confirm: 'Save' },
  );
  /** From Fixed / Verified a fix adds one more deployment (e.g. LIVE). */
  protected readonly title = computed(() =>
    this.kind === 'MarkFixed' && this.data.issue.status !== 'InProgress'
      ? 'Add deployment'
      : this.copy().title,
  );
  protected readonly deploymentName = computed(() =>
    'deployment' in this.data.action ? this.data.action.deployment.environmentName : '',
  );

  protected toggleEnvironment(id: number, checked: boolean): void {
    const next = new Set(this.environmentIds());
    if (checked) {
      next.add(id);
    } else {
      next.delete(id);
    }
    this.environmentIds.set(next);
  }

  protected run(): void {
    if (this.busy()) {
      return;
    }
    const problem = this.check();
    if (problem) {
      this.error.set(problem);
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.request()
      .pipe(concatMap((issue) => this.attachScreenshots(issue)))
      .subscribe({
        next: (issue) => this.ref.close(issue),
        error: (error: unknown) => {
          this.busy.set(false);
          const p = problemOf(error);
          this.error.set(
            p?.errors ? Object.values(p.errors).flat().join(' ') : problemMessage(error),
          );
        },
      });
  }

  private check(): string | null {
    const reason = this.reason.trim();
    const action = this.data.action;
    const needsReason =
      action.action === 'RetestFailed' ||
      ('requires' in action && action.requires.includes('reason'));
    if (this.kind === 'Assign' && !this.assigneeId) {
      return 'Choose who should work on the issue.';
    }
    if (this.kind === 'Reject' && !this.rejectType) {
      return 'Choose why the issue is rejected.';
    }
    if (this.kind === 'Reject' && this.rejectType === 'Duplicate' && !this.duplicateOfKey.trim()) {
      return 'Enter the key of the issue this one duplicates.';
    }
    if (this.kind === 'MarkFixed' && this.environmentIds().size === 0) {
      return 'Choose at least one environment.';
    }
    if (needsReason && !reason) {
      return `${this.copy().reasonLabel ?? 'A reason'} is required.`;
    }
    return null;
  }

  private request(): Observable<Issue> {
    const issue = this.data.issue;
    const reason = this.reason.trim() || null;
    const action = this.data.action;
    if (action.action === 'RetestFailed') {
      return this.issues.retest(action.deployment.id, false, reason);
    }
    switch (action.action) {
      case 'Assign':
        return this.issues.assign(issue, this.assigneeId!, reason);
      case 'MarkFixed':
        return this.issues.markFixed(issue, reason ?? '', [...this.environmentIds()], this.version);
      default:
        return this.issues.transition(issue, action.action, {
          reason,
          rejectType: this.rejectType,
          duplicateOfKey: this.rejectType === 'Duplicate' ? this.duplicateOfKey.trim() : null,
        });
    }
  }

  /** Reopen screenshots go to the timeline as a comment (issue attachments are fixed once testing completed). */
  private attachScreenshots(issue: Issue): Observable<Issue> {
    const files = this.files();
    if (!files.length) {
      return of(issue);
    }
    return this.timeline.comment(issue.key, '', files).pipe(map(() => issue));
  }

  /** A fix usually goes to the environment the issue was found on. */
  private defaultEnvironments(): number[] {
    const found = this.data.issue.foundOnEnvironmentId;
    const environments = this.data.environments;
    if (this.data.issue.status !== 'InProgress') {
      return []; // adding a further deployment (e.g. LIVE): let the developer choose
    }
    if (found && environments.some((e) => e.id === found)) {
      return [found];
    }
    return environments.length === 1 ? [environments[0].id] : [];
  }
}
