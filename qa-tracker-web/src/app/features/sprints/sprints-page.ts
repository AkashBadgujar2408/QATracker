import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { MatOption, MatSelect } from '@angular/material/select';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideChartColumn,
  lucideChevronDown,
  lucideEllipsisVertical,
  lucideHistory,
  lucideList,
} from '@ng-icons/lucide';
import { catchError, debounceTime, firstValueFrom, merge, of } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { problemMessage } from '../../core/api/api';
import { AuthService } from '../../core/auth/auth.service';
import { ToastService } from '../../core/feedback/toast.service';
import { RealtimeService } from '../../core/realtime/realtime.service';
import { MastersApi } from '../../shared/data/masters.api';
import { Sprint, SprintEvent, SprintEventKind, SprintsApi } from '../../shared/data/sprints.api';
import { IstDatePipe } from '../../shared/pipes/ist-date.pipe';
import { PluralPipe } from '../../shared/pipes/plural.pipe';
import { SprintReportButton } from '../../shared/reports/sprint-report-button';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';
import { SprintStatusBadge } from '../../shared/ui/sprint-status/sprint-status';
import {
  CompleteTestingDialog,
  CompleteTestingDialogData,
} from '../testing/complete-testing-dialog';
import { StartSprintDialog, StartSprintDialogData } from '../testing/start-sprint-dialog';
import { TestingContext } from '../testing/testing-context';
import {
  CloseSprintDialog,
  CloseSprintDialogData,
  CloseSprintDialogResult,
} from './close-sprint-dialog';
import { ReopenTestingDialog, ReopenTestingDialogData } from './reopen-testing-dialog';

/** One stop of the lifecycle track (plan 6): reached with a date, or still ahead. */
export interface LifecycleStep {
  readonly label: string;
  readonly at: string | null;
  readonly current: boolean;
}

export function lifecycle(s: Sprint): LifecycleStep[] {
  return [
    { label: 'Started', at: s.startedAt, current: s.status === 'Testing' },
    {
      label: 'Testing completed',
      at: s.testingCompletedAt,
      current: s.status === 'TestingCompleted',
    },
    { label: 'Closed', at: s.closedAt, current: s.status === 'Closed' },
  ];
}

/** Share of the sprint's issues that are Closed or Rejected, 0–100. */
export function donePercent(s: Sprint): number {
  return s.issueCount === 0
    ? 0
    : Math.round(((s.issueCount - s.openIssueCount) / s.issueCount) * 100);
}

export const EVENT_LABELS: Readonly<Record<SprintEventKind, string>> = {
  Started: 'started the sprint',
  TestingCompleted: 'marked testing completed',
  ReopenedForTesting: 'reopened testing',
  Closed: 'closed the sprint',
  IssuesCarriedForward: 'carried issues forward',
  IssuesCarriedIn: 'carried issues in',
};

function id(value: string | null): number | null {
  const n = Number(value);
  return Number.isInteger(n) && n > 0 ? n : null;
}

/**
 * Sprints (plan 8.2): the sprints of one project, newest first – state, lifecycle dates, progress and counts, with
 * the next step beside each sprint (Mark testing completed / Close sprint), carry forward and the admin's reopen
 * testing, the report, the dashboard and the sprint's history (ADR-039).
 */
@Component({
  selector: 'app-sprints-page',
  imports: [
    RouterLink,
    MatButton,
    MatFormField,
    MatLabel,
    MatSelect,
    MatOption,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    NgIcon,
    IstDatePipe,
    PluralPipe,
    EmptyState,
    PageHeader,
    SkeletonLines,
    SprintReportButton,
    SprintStatusBadge,
  ],
  viewProviders: [
    provideIcons({
      lucideChartColumn,
      lucideChevronDown,
      lucideEllipsisVertical,
      lucideHistory,
      lucideList,
    }),
  ],
  templateUrl: './sprints-page.html',
  styleUrl: './sprints-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SprintsPage {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly masters = inject(MastersApi);
  private readonly api = inject(SprintsApi);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);

  protected readonly canManage = this.auth.hasRole('Tester', 'Admin');
  protected readonly isAdmin = this.auth.hasRole('Admin');
  protected readonly eventLabels = EVENT_LABELS;
  protected readonly lifecycle = lifecycle;
  protected readonly donePercent = donePercent;

  private readonly params = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  protected readonly clientId = computed(() => id(this.params().get('client')));
  protected readonly projectId = computed(() => id(this.params().get('project')));

  protected readonly cascade = rxResource({
    params: () => ({ clientId: this.clientId(), projectId: this.projectId() }),
    stream: ({ params }) =>
      this.masters.cascade(params.clientId, params.projectId).pipe(catchError(() => of(null))),
  });
  protected readonly project = computed(
    () => this.cascade.value()?.projects.find((p) => p.id === this.projectId()) ?? null,
  );

  protected readonly sprints = rxResource({
    params: () => this.projectId() ?? undefined,
    stream: ({ params }) => this.api.list(params),
  });
  protected readonly loadError = computed(() => problemMessage(this.sprints.error()));

  /** The sprint whose history is open (one at a time). */
  protected readonly historyOf = signal<number | null>(null);
  protected readonly history = rxResource({
    params: () => this.historyOf() ?? undefined,
    stream: ({ params }) => this.api.get(params),
  });

  constructor() {
    // Start where the Testing screen was left (remembered per user) when the URL names nothing.
    if (!this.clientId() && !this.projectId()) {
      const remembered = inject(TestingContext).load();
      if (remembered.clientId) {
        this.choose({ client: remembered.clientId, project: remembered.projectId });
      }
    }
    // Another user completing, closing or carrying issues refreshes the list (and an open history).
    const realtime = inject(RealtimeService);
    merge(realtime.sprintChanged$, realtime.issueChanged$, realtime.reconnected$)
      .pipe(debounceTime(400), takeUntilDestroyed())
      .subscribe(() => this.refresh());
  }

  protected choose(params: { client: number | null; project: number | null }): void {
    this.historyOf.set(null);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: params,
      replaceUrl: true,
    });
  }

  protected toggleHistory(sprint: Sprint): void {
    this.historyOf.update((current) => (current === sprint.id ? null : sprint.id));
  }

  protected eventText(e: SprintEvent): string {
    return `${e.userName} ${EVENT_LABELS[e.kind]}`;
  }

  protected async completeTesting(sprint: Sprint): Promise<void> {
    const ref = this.dialog.open<CompleteTestingDialog, CompleteTestingDialogData, Sprint>(
      CompleteTestingDialog,
      { data: { sprint }, width: '560px', maxWidth: 'calc(100vw - 32px)', restoreFocus: true },
    );
    const updated = await firstValueFrom(ref.afterClosed());
    if (updated) {
      this.toast.success(`${updated.name}: testing completed. Developers can start fixing.`);
      this.refresh();
    }
  }

  protected async close(sprint: Sprint, mode: 'close' | 'carry' = 'close'): Promise<void> {
    const ref = this.dialog.open<CloseSprintDialog, CloseSprintDialogData, CloseSprintDialogResult>(
      CloseSprintDialog,
      {
        data: { mode, sprint, sprints: this.sprints.value() ?? [] },
        width: '640px',
        maxWidth: 'calc(100vw - 32px)',
        restoreFocus: true,
      },
    );
    const result = await firstValueFrom(ref.afterClosed());
    if (!result) {
      return;
    }
    if (result.kind === 'start') {
      const started = await this.startSprint(sprint);
      if (started) {
        // Back to the same step with the new sprint as the target.
        const fresh = (this.sprints.value() ?? []).find((s) => s.id === sprint.id) ?? sprint;
        await this.close(fresh, mode);
      }
      return;
    }
    if (result.kind === 'closed') {
      this.toast.success(
        result.carried > 0 && result.target
          ? `${result.sprint.name} closed. ${result.carried === 1 ? '1 issue' : `${result.carried} issues`} carried forward to ${result.target.name}.`
          : `${result.sprint.name} closed.`,
      );
    } else {
      const n = result.result.carriedCount;
      this.toast.success(
        `${n === 1 ? '1 issue' : `${n} issues`} carried forward to ${result.result.target.name}.`,
      );
    }
    this.refresh();
  }

  protected async reopen(sprint: Sprint): Promise<void> {
    const ref = this.dialog.open<ReopenTestingDialog, ReopenTestingDialogData, Sprint>(
      ReopenTestingDialog,
      { data: { sprint }, width: '480px', maxWidth: 'calc(100vw - 32px)', restoreFocus: true },
    );
    const updated = await firstValueFrom(ref.afterClosed());
    if (updated) {
      this.toast.success(`${updated.name} is back in testing.`);
      this.refresh();
    }
  }

  /** "Start new sprint" from the close dialog: phase and environment carry over from the sprint being closed. */
  private async startSprint(from: Sprint): Promise<Sprint | undefined> {
    const project = this.project();
    const environments = this.cascade.value()?.environments ?? [];
    if (!project) {
      return undefined;
    }
    const ref = this.dialog.open<StartSprintDialog, StartSprintDialogData, Sprint>(
      StartSprintDialog,
      {
        data: {
          projectId: project.id,
          projectName: project.name,
          environments,
          phaseId: from.phaseId,
          defaultEnvironmentId: from.defaultEnvironmentId,
        },
        width: '480px',
        maxWidth: 'calc(100vw - 32px)',
        restoreFocus: true,
      },
    );
    const sprint = await firstValueFrom(ref.afterClosed());
    if (sprint) {
      this.toast.success(`${sprint.name} started.`);
      this.sprints.set([sprint, ...(this.sprints.value() ?? [])]);
      this.sprints.reload();
    }
    return sprint;
  }

  private refresh(): void {
    if (this.projectId()) {
      this.sprints.reload();
    }
    if (this.historyOf()) {
      this.history.reload();
    }
  }
}
