import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleCheck,
  lucideCircleX,
  lucideClock,
  lucideExternalLink,
  lucideInfo,
} from '@ng-icons/lucide';
import { catchError, firstValueFrom, map, of } from 'rxjs';
import { problemMessage, problemOf } from '../../core/api/api';
import { AuthService } from '../../core/auth/auth.service';
import { ConfirmService } from '../../core/feedback/confirm.service';
import { ToastService } from '../../core/feedback/toast.service';
import { reloadOnIssueChanges } from '../../core/realtime/realtime.service';
import { BreadcrumbService } from '../../core/layout/breadcrumb.service';
import { IssueAttachments } from '../../shared/attachments/issue-attachments';
import {
  AllowedAction,
  Deployment,
  Issue,
  IssueAction,
  IssuesApi,
} from '../../shared/data/issues.api';
import { MastersApi } from '../../shared/data/masters.api';
import { SPRINT_STATUS_LABELS } from '../../shared/data/sprints.api';
import { ISSUE_STATUS_META } from '../../shared/models/issue-enums';
import { IstDatePipe } from '../../shared/pipes/ist-date.pipe';
import { IssueTimeline } from '../../shared/timeline/issue-timeline';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PriorityIcon } from '../../shared/ui/priority-icon/priority-icon';
import { SeverityBadge } from '../../shared/ui/severity-badge/severity-badge';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';
import { StatusPill } from '../../shared/ui/status-pill/status-pill';
import { UserAvatar } from '../../shared/ui/user-avatar/user-avatar';
import { ActionDialog, ActionDialogData } from './action-dialog';
import { OverdueBadge } from '../../shared/ui/overdue-badge/overdue-badge';

/** Button text per action; MarkFixed from Fixed/Verified adds a deployment. */
export function actionLabel(
  action: IssueAction,
  issue: Pick<Issue, 'status' | 'assigneeId'>,
): string {
  switch (action) {
    case 'Assign':
      return issue.assigneeId ? 'Reassign' : 'Assign';
    case 'Pick':
      return 'Pick';
    case 'Start':
      return 'Start work';
    case 'NeedInfo':
      return 'Need info';
    case 'Resume':
      return 'Resume';
    case 'Hold':
      return 'Put on hold';
    case 'Unhold':
      return 'Take off hold';
    case 'Dispute':
      return 'Not a bug';
    case 'Reject':
      return 'Reject';
    case 'Contest':
      return 'Still a bug';
    case 'MarkFixed':
      return issue.status === 'InProgress' ? 'Mark fixed' : 'Add deployment';
    case 'Verify':
      return 'Verify';
    case 'Reopen':
      return 'Reopen';
    case 'Close':
      return 'Close';
  }
}

/** The step that moves the issue forward gets the filled button. */
const PRIMARY: readonly IssueAction[] = [
  'Pick',
  'Start',
  'Resume',
  'MarkFixed',
  'Verify',
  'Close',
  'Unhold',
];

/** Actions that need no input run at once. */
export const DIRECT_ACTIONS: ReadonlySet<IssueAction> = new Set([
  'Pick',
  'Start',
  'Resume',
  'Unhold',
  'Verify',
  'Close',
]);

export interface EnvironmentState {
  readonly environmentId: number;
  readonly name: string;
  readonly url: string;
  /** The latest deployment on this environment. */
  readonly latest: Deployment;
}

/** Environment strip (plan 7.4): the latest deployment per environment, in first-deployed order. */
export function environmentStrip(deployments: readonly Deployment[]): EnvironmentState[] {
  const byEnvironment = new Map<number, EnvironmentState>();
  for (const d of deployments) {
    byEnvironment.set(d.environmentId, {
      environmentId: d.environmentId,
      name: d.environmentName,
      url: d.environmentUrl,
      latest: d,
    });
  }
  return [...byEnvironment.values()];
}

/** Issue detail (plan 8.2): header, environment strip, actions from allowedActions, details, attachments, timeline. */
@Component({
  selector: 'app-issue-detail-page',
  imports: [
    MatButton,
    RouterLink,
    NgIcon,
    IstDatePipe,
    OverdueBadge,
    EmptyState,
    IssueAttachments,
    IssueTimeline,
    PriorityIcon,
    SeverityBadge,
    SkeletonLines,
    StatusPill,
    UserAvatar,
  ],
  viewProviders: [
    provideIcons({ lucideCircleCheck, lucideCircleX, lucideClock, lucideExternalLink, lucideInfo }),
  ],
  templateUrl: './issue-detail-page.html',
  styles: `
    .qa-details {
      display: grid;
      grid-template-columns: 6.5rem minmax(0, 1fr);
      gap: 0.5rem 0.75rem;
      font-size: var(--qa-text-sm);
    }
    .qa-details dt {
      color: var(--qa-text-muted);
    }
    .qa-details dd {
      margin: 0;
      color: var(--qa-text);
      overflow-wrap: anywhere;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IssueDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(IssuesApi);
  private readonly masters = inject(MastersApi);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly breadcrumbs = inject(BreadcrumbService);
  private readonly auth = inject(AuthService);

  protected readonly sprintLabels = SPRINT_STATUS_LABELS;
  protected readonly statusMeta = ISSUE_STATUS_META;

  protected readonly key = toSignal(this.route.paramMap.pipe(map((p) => p.get('key') ?? '')), {
    initialValue: this.route.snapshot.paramMap.get('key') ?? '',
  });

  protected readonly issue = rxResource({
    params: () => this.key(),
    stream: ({ params }) => this.api.get(params),
  });
  protected readonly current = computed(() => (this.issue.hasValue() ? this.issue.value() : null));
  protected readonly notFound = computed(() => problemOf(this.issue.error())?.status === 404);
  protected readonly loadError = computed(() => problemMessage(this.issue.error()));

  protected readonly users = rxResource({
    stream: () => this.masters.users().pipe(catchError(() => of([]))),
  });
  private readonly environments = rxResource({
    params: () => this.current()?.projectId,
    stream: ({ params }) =>
      this.masters.cascade(null, params).pipe(
        map((c) => c.environments),
        catchError(() => of([])),
      ),
  });
  protected readonly related = rxResource({
    params: () => this.current()?.key,
    stream: ({ params }) => this.api.related(params).pipe(catchError(() => of([]))),
  });

  protected readonly busy = signal<IssueAction | null>(null);
  protected readonly actionError = signal<string | null>(null);

  private readonly allowed = computed(() => this.current()?.allowedActions ?? []);
  private readonly pendingRetest = computed(() =>
    (this.current()?.deployments ?? []).some((d) => d.retestResult === 'Pending'),
  );
  /** While a retest is pending, Pass / Fail on the deployment stand in for Verify / Reopen. */
  protected readonly actions = computed(() =>
    this.allowed().filter(
      (a) =>
        !(
          this.pendingRetest() &&
          this.canRetest() &&
          (a.action === 'Verify' || a.action === 'Reopen')
        ),
    ),
  );
  protected readonly primary = computed(() => {
    const allowed = this.actions().map((a) => a.action);
    // Adding a further deployment (from Fixed/Verified) is never the main step.
    const fixing = this.current()?.status === 'InProgress';
    return PRIMARY.find((a) => allowed.includes(a) && (a !== 'MarkFixed' || fixing)) ?? null;
  });
  /** Pass/Fail on pending retests – the tester's Verify/Reopen decides who sees them. */
  protected readonly canRetest = computed(() => {
    const allowed = this.allowed().map((a) => a.action);
    return allowed.includes('Verify') && allowed.includes('Reopen');
  });
  protected readonly strip = computed(() => environmentStrip(this.current()?.deployments ?? []));

  protected readonly developerWaiting = computed(
    () => this.current()?.sprintStatus === 'Testing' && this.auth.role() === 'Developer',
  );
  protected readonly overdue = computed(() => {
    const issue = this.current();
    return (
      !!issue?.dueDate &&
      new Date(issue.dueDate) < new Date() &&
      !['Fixed', 'Verified', 'Closed', 'Rejected'].includes(issue.status)
    );
  });

  private readonly timeline = viewChild(IssueTimeline);

  constructor() {
    // Someone else changed this issue: reload it (keeps the page, refreshes allowed actions and row version).
    reloadOnIssueChanges(
      () => this.issue.reload(),
      (e) => e.issueKey === this.key().trim().toUpperCase(),
    );
    effect(() => {
      const issue = this.current();
      this.breadcrumbs.set([
        { label: 'Issues', link: '/issues' },
        ...(issue
          ? [
              { label: issue.clientName },
              { label: `${issue.projectName} (${issue.projectKey})` },
              { label: issue.sprintName },
              { label: issue.key },
            ]
          : [{ label: this.key() }]),
      ]);
    });
  }

  protected label(action: IssueAction): string {
    const issue = this.current();
    return issue ? actionLabel(action, issue) : action;
  }

  protected async act(allowed: AllowedAction): Promise<void> {
    const issue = this.current();
    if (!issue || this.busy()) {
      return;
    }
    if (DIRECT_ACTIONS.has(allowed.action)) {
      if (allowed.action === 'Close') {
        const ok = await this.confirm.ask({
          title: `Close ${issue.key}?`,
          message:
            'The fix is verified and nothing more is needed. A tester can reopen it if it regresses.',
          confirmLabel: 'Close issue',
        });
        if (!ok) {
          return;
        }
      }
      this.busy.set(allowed.action);
      this.actionError.set(null);
      const request =
        allowed.action === 'Pick'
          ? this.api.pick(issue.key)
          : this.api.transition(issue, allowed.action);
      request.subscribe({
        next: (updated) => this.done(updated, allowed.action),
        error: (error: unknown) => this.failed(error),
      });
      return;
    }
    await this.openDialog({ issue, action: allowed });
  }

  protected retestPassed(deployment: Deployment): void {
    if (this.busy()) {
      return;
    }
    this.busy.set('Verify');
    this.actionError.set(null);
    this.api.retest(deployment.id, true).subscribe({
      next: (updated) =>
        this.done(updated, 'Verify', `Retest passed on ${deployment.environmentName}.`),
      error: (error: unknown) => this.failed(error),
    });
  }

  protected async retestFailed(deployment: Deployment): Promise<void> {
    const issue = this.current();
    if (issue) {
      await this.openDialog({ issue, action: { action: 'RetestFailed', deployment } });
    }
  }

  protected reloadAll(): void {
    this.issue.reload();
    this.timeline()?.reload();
  }

  private async openDialog(data: Pick<ActionDialogData, 'issue' | 'action'>): Promise<void> {
    const users = this.users.value() ?? [];
    const ref = this.dialog.open<ActionDialog, ActionDialogData, Issue>(ActionDialog, {
      data: {
        ...data,
        assignees: users.filter((u) => u.role !== 'Tester'),
        environments: this.environments.value() ?? [],
      },
      width: '520px',
      maxWidth: 'calc(100vw - 32px)',
      restoreFocus: true,
    });
    const updated = await firstValueFrom(ref.afterClosed());
    if (updated) {
      this.done(updated, data.action.action === 'RetestFailed' ? 'Reopen' : data.action.action);
    }
  }

  private done(updated: Issue, action: IssueAction, message?: string): void {
    this.busy.set(null);
    this.issue.set(updated);
    this.related.reload();
    this.timeline()?.reload();
    this.toast.success(message ?? `${updated.key}: ${ISSUE_STATUS_META[updated.status].label}.`);
    if (action === 'NeedInfo') {
      this.toast.info('Your question was posted to the timeline.');
    }
  }

  private failed(error: unknown): void {
    this.busy.set(null);
    const problem = problemOf(error);
    this.actionError.set(
      problem?.status === 409
        ? `${problemMessage(error)} Reload to see the latest version.`
        : problemMessage(error),
    );
  }
}
