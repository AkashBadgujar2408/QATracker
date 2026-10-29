import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { ActivatedRoute, ParamMap, RouterLink } from '@angular/router';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatOption, MatSelect, MatSelectTrigger } from '@angular/material/select';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideChartPie,
  lucideCircleCheck,
  lucideFlaskConical,
  lucideInfo,
  lucidePlus,
} from '@ng-icons/lucide';
import { firstValueFrom, map } from 'rxjs';
import { problemMessage } from '../../core/api/api';
import { ConfirmService } from '../../core/feedback/confirm.service';
import { ToastService } from '../../core/feedback/toast.service';
import { BreadcrumbService } from '../../core/layout/breadcrumb.service';
import { Issue, IssueListItem, IssuesApi, MAX_PAGE_SIZE } from '../../shared/data/issues.api';
import { MastersApi } from '../../shared/data/masters.api';
import { SPRINT_STATUS_LABELS, Sprint, SprintsApi } from '../../shared/data/sprints.api';
import { MasterDialogs } from '../../shared/masters/master-dialogs.service';
import { SEVERITIES, SEVERITY_META, Severity } from '../../shared/models/issue-enums';
import { IstDatePipe, formatIst } from '../../shared/pipes/ist-date.pipe';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { EntityPicker, PickerItem } from '../../shared/ui/entity-picker/entity-picker';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';
import { CompleteTestingDialog, CompleteTestingDialogData } from './complete-testing-dialog';
import { IssueForm, IssueFormMode, IssueSaved } from './issue-form';
import { SprintIssues } from './sprint-issues';
import { StartSprintDialog, StartSprintDialogData } from './start-sprint-dialog';
import { EMPTY_SELECTION, TestingContext, TestingSelection } from './testing-context';

/** Sprint select value that opens the start dialog. */
const START_NEW = -1;

export interface SprintStats {
  readonly total: number;
  readonly today: number;
  readonly bySeverity: readonly { readonly severity: Severity; readonly count: number }[];
  /** "Day 3": days on which issues were logged, counting today while the sprint is in testing. */
  readonly day: number;
}

/** Sprint stat chips (plan 8.1) from the sprint's issues. */
export function sprintStats(
  issues: readonly IssueListItem[],
  inTesting: boolean,
  now = new Date(),
): SprintStats {
  const today = formatIst(now);
  const days = new Set(issues.map((i) => formatIst(i.createdAt)));
  if (inTesting) {
    days.add(today);
  }
  return {
    total: issues.length,
    today: issues.filter((i) => formatIst(i.createdAt) === today).length,
    bySeverity: SEVERITIES.map((severity) => ({
      severity,
      count: issues.filter((i) => i.severity === severity).length,
    })).filter((s) => s.count > 0),
    day: days.size,
  };
}

/** Testing workspace (plan 8.1): context bar, quick issue form (≈ 40 %) and this sprint's issues (≈ 60 %). */
@Component({
  selector: 'app-testing-page',
  imports: [
    MatButton,
    RouterLink,
    MatFormField,
    MatLabel,
    MatSelect,
    MatSelectTrigger,
    MatOption,
    NgIcon,
    IstDatePipe,
    EmptyState,
    EntityPicker,
    PageHeader,
    SkeletonLines,
    IssueForm,
    SprintIssues,
  ],
  viewProviders: [
    provideIcons({ lucideChartPie, lucideCircleCheck, lucideFlaskConical, lucideInfo, lucidePlus }),
  ],
  templateUrl: './testing-page.html',
  styles: `
    .qa-chip,
    .qa-day {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 3px 10px;
      border-radius: var(--qa-radius-pill);
      font-size: var(--qa-text-sm);
      line-height: 20px;
      white-space: nowrap;
    }
    .qa-chip {
      border: 1px solid var(--qa-border);
      background: var(--qa-surface-muted);
      color: var(--qa-text-muted);
    }
    .qa-chip strong {
      color: var(--qa-text);
      font-weight: 600;
    }
    .qa-day {
      font-weight: 600;
      color: var(--qa-text);
      border: 1px solid var(--qa-border-strong);
    }
    .qa-day-live {
      border-color: transparent;
      background: var(--qa-sprint-testing);
      color: var(--qa-primary-contrast);
    }
  `,
  host: { '(document:keydown)': 'onKey($event)' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TestingPage {
  private readonly masters = inject(MastersApi);
  private readonly sprintsApi = inject(SprintsApi);
  private readonly issuesApi = inject(IssuesApi);
  private readonly context = inject(TestingContext);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly breadcrumbs = inject(BreadcrumbService);
  protected readonly masterDialogs = inject(MasterDialogs);

  protected readonly startNew = START_NEW;
  protected readonly statusLabels = SPRINT_STATUS_LABELS;
  protected readonly severityMeta = SEVERITY_META;

  /** A link like /testing?client=1&project=1&sprint=3 (e.g. from the Sprints page) wins over the remembered choice. */
  protected readonly selection = signal<TestingSelection>(
    fromQuery(inject(ActivatedRoute).snapshot.queryParamMap) ?? this.context.load(),
  );
  protected readonly grouped = signal(this.context.loadGrouped());
  protected readonly mode = signal<IssueFormMode>({ kind: 'create' });

  protected readonly cascade = rxResource({
    params: () => ({ clientId: this.selection().clientId, projectId: this.selection().projectId }),
    stream: ({ params }) => this.masters.cascade(params.clientId, params.projectId),
  });

  protected readonly sprints = rxResource({
    params: () => this.selection().projectId ?? undefined,
    stream: ({ params }) => this.sprintsApi.list(params),
  });

  /** The chosen sprint; otherwise the one in Testing; otherwise the newest. */
  protected readonly sprint = computed<Sprint | null>(() => {
    const sprints = this.sprints.hasValue() ? this.sprints.value() : [];
    const id = this.selection().sprintId;
    return (
      sprints.find((s) => s.id === id) ??
      sprints.find((s) => s.status === 'Testing') ??
      sprints[0] ??
      null
    );
  });
  /** Changes only when another sprint is chosen (not when the list reloads). */
  private readonly sprintId = computed(() => this.sprint()?.id ?? null);
  protected readonly hasTestingSprint = computed(() =>
    (this.sprints.value() ?? []).some((s) => s.status === 'Testing'),
  );
  protected readonly inTesting = computed(() => this.sprint()?.status === 'Testing');

  protected readonly issues = rxResource({
    params: () => this.sprintId() ?? undefined,
    stream: ({ params }) =>
      this.issuesApi
        .list({ sprintId: params, size: MAX_PAGE_SIZE, sort: '-createdAt' })
        .pipe(map((page) => page.items)),
  });
  protected readonly issueList = computed(() =>
    this.issues.hasValue() ? this.issues.value() : [],
  );
  protected readonly issuesError = computed(() =>
    this.issues.error() ? problemMessage(this.issues.error()) : null,
  );

  protected readonly stats = computed(() => sprintStats(this.issueList(), this.inTesting()));

  protected readonly clientItems = computed<PickerItem[]>(() =>
    (this.cascade.value()?.clients ?? []).map((c) => ({ id: c.id, label: c.name })),
  );
  protected readonly projectItems = computed<PickerItem[]>(() =>
    (this.cascade.value()?.projects ?? []).map((p) => ({ id: p.id, label: p.name, hint: p.key })),
  );
  private readonly client = computed(() => {
    const id = this.selection().clientId;
    return this.cascade.value()?.clients.find((c) => c.id === id) ?? null;
  });
  private readonly project = computed(() => {
    const id = this.selection().projectId;
    return this.cascade.value()?.projects.find((p) => p.id === id) ?? null;
  });

  protected readonly addClient = this.masterDialogs.quickAdd('Client');
  protected readonly addProject = this.masterDialogs.quickAdd('Project', () => {
    const client = this.client();
    return client ? { id: client.id, name: client.name } : null;
  });

  protected readonly editingKey = computed(() => {
    const mode = this.mode();
    return mode.kind === 'edit' ? mode.issue.key : null;
  });

  private readonly form = viewChild(IssueForm);

  constructor() {
    effect(() => this.context.save(this.selection()));
    effect(() => this.context.saveGrouped(this.grouped()));

    // Remembered ids may be gone (deactivated, no access): drop what the cascade no longer has.
    effect(() => {
      const cascade = this.cascade.value();
      if (!cascade) {
        return;
      }
      const { clientId, projectId } = untracked(this.selection);
      if (clientId && !cascade.clients.some((c) => c.id === clientId)) {
        this.selection.set(EMPTY_SELECTION);
      } else if (clientId && projectId && !cascade.projects.some((p) => p.id === projectId)) {
        this.selection.set({ clientId, projectId: null, sprintId: null });
      }
    });

    effect(() => {
      const client = this.client();
      const project = this.project();
      const sprint = this.sprint();
      this.breadcrumbs.set([
        { label: 'Testing' },
        ...(client ? [{ label: client.name }] : []),
        ...(project ? [{ label: `${project.name} (${project.key})` }] : []),
        ...(project && sprint ? [{ label: sprint.name }] : []),
      ]);
    });

    // Leave edit / clone when the sprint changes.
    effect(() => {
      this.sprintId();
      untracked(() => this.mode.set({ kind: 'create' }));
    });
  }

  protected chooseClient(clientId: number | null): void {
    if (clientId !== this.selection().clientId) {
      this.selection.set({ clientId, projectId: null, sprintId: null });
    }
  }

  protected chooseProject(projectId: number | null): void {
    if (projectId !== this.selection().projectId) {
      this.selection.update((s) => ({ ...s, projectId, sprintId: null }));
    }
  }

  protected chooseSprint(sprintId: number): void {
    if (sprintId === START_NEW) {
      void this.startSprint();
      return;
    }
    this.selection.update((s) => ({ ...s, sprintId }));
  }

  /** Inline "+ Start new sprint" (plan 6.1); after a completed sprint the phase and environment carry over. */
  protected async startSprint(): Promise<void> {
    const project = this.project();
    const cascade = this.cascade.value();
    if (!project || !cascade) {
      return;
    }
    const previous = this.sprint();
    const ref = this.dialog.open<StartSprintDialog, StartSprintDialogData, Sprint>(
      StartSprintDialog,
      {
        data: {
          projectId: project.id,
          projectName: project.name,
          environments: cascade.environments,
          phaseId: previous?.phaseId,
          defaultEnvironmentId: previous?.defaultEnvironmentId,
        },
        width: '480px',
        maxWidth: 'calc(100vw - 32px)',
        restoreFocus: true,
      },
    );
    const sprint = await firstValueFrom(ref.afterClosed());
    if (sprint) {
      this.toast.success(`${sprint.name} started.`);
      this.selection.update((s) => ({ ...s, sprintId: sprint.id }));
      this.sprints.reload();
    }
  }

  protected async completeTesting(): Promise<void> {
    const sprint = this.sprint();
    if (!sprint) {
      return;
    }
    const ref = this.dialog.open<CompleteTestingDialog, CompleteTestingDialogData, Sprint>(
      CompleteTestingDialog,
      { data: { sprint }, width: '560px', maxWidth: 'calc(100vw - 32px)', restoreFocus: true },
    );
    const updated = await firstValueFrom(ref.afterClosed());
    if (updated) {
      this.toast.success(`${updated.name}: testing completed. Developers can start fixing.`);
      this.sprints.reload();
    }
  }

  protected edit(item: IssueListItem): void {
    this.issuesApi.get(item.key).subscribe({
      next: (issue) => this.mode.set({ kind: 'edit', issue }),
      error: (error: unknown) => this.toast.error(problemMessage(error)),
    });
  }

  protected clone(item: IssueListItem): void {
    this.issuesApi.get(item.key).subscribe({
      next: (issue) => this.mode.set({ kind: 'create', cloneOf: issue }),
      error: (error: unknown) => this.toast.error(problemMessage(error)),
    });
  }

  protected async remove(item: IssueListItem): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Delete ${item.key}?`,
      message: `“${item.title}” and its attachments are removed for good. The number ${item.key} may be reused.`,
      confirmLabel: 'Delete issue',
      danger: true,
    });
    if (!ok) {
      return;
    }
    this.issuesApi.delete(item.key).subscribe({
      next: () => {
        this.toast.success(`${item.key} deleted.`);
        if (this.editingKey() === item.key) {
          this.mode.set({ kind: 'create' });
        }
        this.refreshIssues();
      },
      error: (error: unknown) => this.toast.error(problemMessage(error)),
    });
  }

  protected onSaved(saved: IssueSaved): void {
    if (!saved.created) {
      this.mode.set({ kind: 'create' });
    }
    this.refreshIssues(saved.issue);
  }

  protected onKey(event: KeyboardEvent): void {
    if (
      event.key.toLowerCase() !== 'n' ||
      event.ctrlKey ||
      event.metaKey ||
      event.altKey ||
      this.dialog.openDialogs.length > 0 ||
      isTyping(event.target) ||
      !this.inTesting()
    ) {
      return;
    }
    event.preventDefault();
    const mode = this.mode();
    if (mode.kind === 'edit' || mode.cloneOf) {
      this.mode.set({ kind: 'create' });
    }
    queueMicrotask(() => this.form()?.focusFirst());
  }

  /** Shows a saved issue at once, then reloads the list and sprint counts. */
  private refreshIssues(saved?: Issue): void {
    if (saved && this.issues.hasValue()) {
      const rest = this.issues.value().filter((i) => i.id !== saved.id);
      this.issues.set([toListItem(saved), ...rest]);
    }
    this.issues.reload();
    this.sprints.reload();
  }
}

function fromQuery(params: ParamMap): TestingSelection | null {
  const id = (name: string) => {
    const value = Number(params.get(name));
    return Number.isInteger(value) && value > 0 ? value : null;
  };
  const selection = { clientId: id('client'), projectId: id('project'), sprintId: id('sprint') };
  return selection.clientId && selection.projectId ? selection : null;
}

function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  return (
    target.isContentEditable ||
    ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName) ||
    target.closest('[role="listbox"], [role="combobox"], .mat-mdc-select') !== null
  );
}

function toListItem(issue: Issue): IssueListItem {
  return {
    id: issue.id,
    key: issue.key,
    title: issue.title,
    projectId: issue.projectId,
    projectKey: issue.projectKey,
    sprintId: issue.sprintId,
    sprintName: issue.sprintName,
    moduleName: issue.moduleName,
    screenName: issue.screenName,
    type: issue.type,
    severity: issue.severity,
    priority: issue.priority,
    status: issue.status,
    assigneeId: issue.assigneeId,
    assigneeName: issue.assigneeName,
    reporterName: issue.reporterName,
    dueDate: issue.dueDate,
    createdAt: issue.createdAt,
    updatedAt: issue.updatedAt,
  };
}
