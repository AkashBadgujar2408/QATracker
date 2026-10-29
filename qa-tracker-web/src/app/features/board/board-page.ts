import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { rxResource, takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { CdkDrag, CdkDragDrop, CdkDropList, CdkDropListGroup } from '@angular/cdk/drag-drop';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormField, MatLabel, MatPrefix } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch } from '@ng-icons/lucide';
import { Observable, Subject, catchError, debounceTime, firstValueFrom, map, of } from 'rxjs';
import { problemMessage, problemOf } from '../../core/api/api';
import { AuthService } from '../../core/auth/auth.service';
import { ConfirmService } from '../../core/feedback/confirm.service';
import { ToastService } from '../../core/feedback/toast.service';
import { reloadOnIssueChanges } from '../../core/realtime/realtime.service';
import { BoardApi, BoardCard, BoardColumn } from '../../shared/data/board.api';
import { AllowedAction, Issue, IssuesApi } from '../../shared/data/issues.api';
import { MastersApi } from '../../shared/data/masters.api';
import { SprintsApi } from '../../shared/data/sprints.api';
import {
  ISSUE_STATUS_META,
  IssueStatus,
  SEVERITIES,
  SEVERITY_META,
} from '../../shared/models/issue-enums';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { ActionDialog, ActionDialogData } from '../issues/action-dialog';
import { DIRECT_ACTIONS, actionLabel } from '../issues/issue-detail-page';
import { BoardCardView } from './board-card';
import {
  BoardFilters,
  PendingMove,
  boardFiltersFromParams,
  boardFiltersToParams,
  boardQuery,
  moveFor,
  refusal,
  visibleColumns,
} from './board-logic';

const LAST_PROJECT_KEY = 'qa.board.project';

/**
 * Kanban board (plan S20): a column per status of one project. Dragging a card runs the workflow action that leads
 * to the column – only the moves the API lists (allowedActions) are accepted; the rest are refused with the reason.
 * Actions that need input open the same dialog as the issue page. Other users' changes arrive live.
 */
@Component({
  selector: 'app-board-page',
  imports: [
    CdkDrag,
    CdkDropList,
    CdkDropListGroup,
    MatButton,
    MatFormField,
    MatLabel,
    MatPrefix,
    MatInput,
    MatSelect,
    MatOption,
    MatSlideToggle,
    RouterLink,
    NgIcon,
    EmptyState,
    PageHeader,
    BoardCardView,
  ],
  viewProviders: [provideIcons({ lucideSearch })],
  templateUrl: './board-page.html',
  styleUrl: './board-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BoardPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly boardApi = inject(BoardApi);
  private readonly issues = inject(IssuesApi);
  private readonly masters = inject(MastersApi);
  private readonly sprintsApi = inject(SprintsApi);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly dialog = inject(MatDialog);

  protected readonly severities = SEVERITIES;
  protected readonly severityMeta = SEVERITY_META;
  protected readonly statusMeta = ISSUE_STATUS_META;

  private readonly params = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  protected readonly filters = computed(() => boardFiltersFromParams(this.params()));

  protected readonly cascade = rxResource({
    params: () => ({ clientId: this.filters().clientId, projectId: this.filters().projectId }),
    stream: ({ params }) => this.masters.cascade(params.clientId, params.projectId),
  });
  protected readonly sprints = rxResource({
    params: () => this.filters().projectId ?? undefined,
    stream: ({ params }) => this.sprintsApi.list(params).pipe(catchError(() => of([]))),
  });
  private readonly users = rxResource({
    stream: () => this.masters.users().pipe(catchError(() => of([]))),
  });
  protected readonly developers = computed(() =>
    (this.users.value() ?? []).filter((u) => u.role !== 'Tester'),
  );

  protected readonly board = rxResource({
    params: () => {
      const f = this.filters();
      return f.projectId ? { ...f, projectId: f.projectId } : undefined;
    },
    stream: ({ params }) => this.boardApi.get(boardQuery(params)),
  });

  protected readonly pending = signal<PendingMove | null>(null);
  protected readonly dragging = signal<BoardCard | null>(null);
  protected readonly columns = computed(() =>
    this.board.hasValue()
      ? visibleColumns(this.board.value(), this.filters().done, this.pending())
      : [],
  );
  protected readonly total = computed(() =>
    this.board.hasValue() ? this.board.value().columns.reduce((n, c) => n + c.count, 0) : 0,
  );
  protected readonly problem = computed(() => problemMessage(this.board.error()));
  protected readonly search = signal('');
  private readonly searches = new Subject<string>();

  constructor() {
    this.restoreLastProject();
    effect(() => {
      const q = this.filters().q;
      untracked(() => this.search.set(q));
    });
    this.searches
      .pipe(debounceTime(300), takeUntilDestroyed())
      .subscribe((q) => this.update({ q }));
    reloadOnIssueChanges(
      () => {
        if (!this.pending()) {
          this.board.reload();
        }
      },
      (e) => e.projectId === this.filters().projectId,
    );
  }

  // Filters

  protected update(patch: Partial<BoardFilters>): void {
    const next = { ...this.filters(), ...patch };
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: boardFiltersToParams(next),
      replaceUrl: true,
    });
  }

  protected chooseClient(clientId: number | null): void {
    this.update({ clientId, projectId: null, sprintId: null });
  }

  protected chooseProject(projectId: number | null): void {
    this.update({ projectId, sprintId: null });
    this.remember(this.filters().clientId, projectId);
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.searches.next(value);
  }

  // Drag and drop

  protected columnLabel(status: IssueStatus): string {
    return ISSUE_STATUS_META[status].label;
  }

  protected columnColor(status: IssueStatus): string {
    return `var(${ISSUE_STATUS_META[status].colorToken})`;
  }

  /** While dragging: the action the column would run, or null when the card cannot go there. */
  protected dropHint(column: BoardColumn): string | null {
    const card = this.dragging();
    if (!card || column.status === card.status) {
      return null;
    }
    const move = moveFor(card, column.status);
    return move ? this.moveLabel(card, move) : null;
  }

  protected dropState(column: BoardColumn): 'home' | 'open' | 'closed' | null {
    const card = this.dragging();
    if (!card) {
      return null;
    }
    if (column.status === card.status) {
      return 'home';
    }
    return moveFor(card, column.status) ? 'open' : 'closed';
  }

  protected dropped(event: CdkDragDrop<IssueStatus, IssueStatus, BoardCard>): void {
    this.dragging.set(null);
    const card = event.item.data;
    const target = event.container.data;
    if (target === card.status) {
      return;
    }
    const move = moveFor(card, target);
    if (!move) {
      this.toast.warn(refusal(card, target, this.auth.role()));
      return;
    }
    void this.run(card, move);
  }

  /** A move from the drag or the card menu. */
  protected async run(card: BoardCard, move: AllowedAction): Promise<void> {
    if (this.pending()) {
      return;
    }
    if (move.action === 'Close') {
      const ok = await this.confirm.ask({
        title: `Close ${card.key}?`,
        message:
          'The fix is verified and nothing more is needed. A tester can reopen it if it regresses.',
        confirmLabel: 'Close issue',
      });
      if (!ok) {
        return;
      }
    }
    this.pending.set({ key: card.key, to: move.target });
    const request = DIRECT_ACTIONS.has(move.action)
      ? move.action === 'Pick'
        ? this.issues.pick(card.key)
        : this.issues.transition(card, move.action)
      : this.withDialog(card, move);
    request.subscribe({
      next: (updated) => {
        if (updated) {
          this.toast.success(`${updated.key}: ${ISSUE_STATUS_META[updated.status].label}.`);
        }
        this.settle();
      },
      error: (error: unknown) => {
        this.toast.error(
          problemOf(error)?.status === 409
            ? `${problemMessage(error)} The board shows the latest version now.`
            : `Could not move ${card.key}: ${problemMessage(error)}`,
        );
        this.settle();
      },
    });
  }

  private moveLabel(card: BoardCard, move: AllowedAction): string {
    return actionLabel(move.action, card);
  }

  /** Loads the issue (fresh version and actions) and asks for what the action needs; null = cancelled. */
  private withDialog(card: BoardCard, move: AllowedAction): Observable<Issue | null> {
    return new Observable<Issue | null>((subscriber) => {
      const run = async (): Promise<void> => {
        const [issue, environments] = await Promise.all([
          firstValueFrom(this.issues.get(card.key)),
          firstValueFrom(
            this.masters.cascade(null, this.filters().projectId).pipe(
              map((c) => c.environments),
              catchError(() => of([])),
            ),
          ),
        ]);
        const fresh = issue.allowedActions.find((a) => a.action === move.action);
        if (!fresh) {
          this.toast.warn(`${card.key} changed meanwhile – that move is no longer possible.`);
          return subscriber.next(null);
        }
        const ref = this.dialog.open<ActionDialog, ActionDialogData, Issue>(ActionDialog, {
          data: { issue, action: fresh, assignees: this.developers(), environments },
          width: '520px',
          maxWidth: 'calc(100vw - 32px)',
          restoreFocus: true,
        });
        subscriber.next((await firstValueFrom(ref.afterClosed())) ?? null);
      };
      run().then(
        () => subscriber.complete(),
        (error: unknown) => subscriber.error(error),
      );
    });
  }

  private settle(): void {
    this.pending.set(null);
    this.board.reload();
  }

  // Last project (per browser; storage may be unavailable)

  private restoreLastProject(): void {
    if (this.filters().projectId) {
      this.remember(this.filters().clientId, this.filters().projectId);
      return;
    }
    try {
      const saved = JSON.parse(localStorage.getItem(LAST_PROJECT_KEY) ?? 'null') as {
        clientId: number;
        projectId: number;
      } | null;
      if (saved?.clientId && saved.projectId) {
        this.update({ clientId: saved.clientId, projectId: saved.projectId });
      }
    } catch {
      // nothing remembered
    }
  }

  private remember(clientId: number | null, projectId: number | null): void {
    try {
      if (clientId && projectId) {
        localStorage.setItem(LAST_PROJECT_KEY, JSON.stringify({ clientId, projectId }));
      }
    } catch {
      // not remembered – fine
    }
  }
}
