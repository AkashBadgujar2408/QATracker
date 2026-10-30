import { HttpErrorResponse } from '@angular/common/http';
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
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormField, MatLabel, MatPrefix } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { MatPaginator, PageEvent } from '@angular/material/paginator';
import { MatOption, MatSelect } from '@angular/material/select';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { MatSort, MatSortHeader, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBookmark,
  lucideDownload,
  lucideSearch,
  lucideTrash2,
  lucideX,
} from '@ng-icons/lucide';
import { Subject, catchError, debounceTime, firstValueFrom, of } from 'rxjs';
import { problemMessage } from '../../core/api/api';
import { ToastService } from '../../core/feedback/toast.service';
import { reloadOnIssueChanges } from '../../core/realtime/realtime.service';
import { IssueListItem, IssuesApi } from '../../shared/data/issues.api';
import { saveFile } from '../../shared/data/download';
import { MastersApi } from '../../shared/data/masters.api';
import { SprintsApi } from '../../shared/data/sprints.api';
import {
  ISSUE_STATUSES,
  ISSUE_STATUS_META,
  PRIORITIES,
  PRIORITY_META,
  SEVERITIES,
  SEVERITY_META,
} from '../../shared/models/issue-enums';
import { IstDatePipe } from '../../shared/pipes/ist-date.pipe';
import { PluralPipe } from '../../shared/pipes/plural.pipe';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { PriorityIcon } from '../../shared/ui/priority-icon/priority-icon';
import { SeverityBadge } from '../../shared/ui/severity-badge/severity-badge';
import { SkeletonTable } from '../../shared/ui/skeleton/skeleton';
import { StatusPill } from '../../shared/ui/status-pill/status-pill';
import { UserAvatar } from '../../shared/ui/user-avatar/user-avatar';
import { OverdueBadge } from '../../shared/ui/overdue-badge/overdue-badge';
import {
  DEFAULT_SORT,
  EMPTY_FILTERS,
  IssueFilters,
  PAGE_SIZES,
  SavedView,
  SavedViews,
  activeFilterCount,
  filtersFromParams,
  filtersToParams,
  filtersToQuery,
} from './issue-filters';

/** Asks for the name of a saved view. */
@Component({
  selector: 'app-save-view-dialog',
  imports: [FormsModule, MatDialogModule, MatButton, MatFormField, MatLabel, MatInput],
  template: `
    <h2 mat-dialog-title>Save this view</h2>
    <mat-dialog-content>
      <mat-form-field appearance="outline" class="w-full" subscriptSizing="dynamic">
        <mat-label>Name</mat-label>
        <input matInput [(ngModel)]="name" maxlength="60" cdkFocusInitial autocomplete="off" />
      </mat-form-field>
      <p class="m-0 mt-2 text-sm text-muted">Filters and sort are kept in this browser.</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" mat-dialog-close>Cancel</button>
      <button
        matButton="filled"
        type="button"
        [disabled]="!name.trim()"
        [mat-dialog-close]="name.trim()"
      >
        Save view
      </button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SaveViewDialog {
  protected name = inject<string>(MAT_DIALOG_DATA);
}

/** Issue list (plan 8.2): filter bar, table, saved views, bulk assign; a row opens the issue. */
@Component({
  selector: 'app-issue-list-page',
  imports: [
    FormsModule,
    MatButton,
    MatCheckbox,
    MatFormField,
    MatLabel,
    MatPrefix,
    MatInput,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    MatPaginator,
    MatSelect,
    MatOption,
    MatSlideToggle,
    MatSort,
    MatSortHeader,
    MatTableModule,
    RouterLink,
    NgIcon,
    IstDatePipe,
    OverdueBadge,
    PluralPipe,
    EmptyState,
    PageHeader,
    PriorityIcon,
    SeverityBadge,
    SkeletonTable,
    StatusPill,
    UserAvatar,
  ],
  viewProviders: [
    provideIcons({ lucideBookmark, lucideDownload, lucideSearch, lucideTrash2, lucideX }),
  ],
  templateUrl: './issue-list-page.html',
  styles: `
    .qa-issue-table .mat-mdc-row:hover {
      background: var(--qa-surface-muted);
    }
    .qa-issue-table .qa-row-selected {
      background: color-mix(in srgb, var(--qa-primary) 6%, transparent);
    }
    .qa-compact {
      --mat-form-field-container-height: 40px;
      --mat-form-field-container-vertical-padding: 8px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IssueListPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly issuesApi = inject(IssuesApi);
  private readonly masters = inject(MastersApi);
  private readonly sprintsApi = inject(SprintsApi);
  private readonly toast = inject(ToastService);
  private readonly dialog = inject(MatDialog);
  private readonly savedViews = inject(SavedViews);

  protected readonly statuses = ISSUE_STATUSES;
  protected readonly statusMeta = ISSUE_STATUS_META;
  protected readonly severities = SEVERITIES;
  protected readonly severityMeta = SEVERITY_META;
  protected readonly priorities = PRIORITIES;
  protected readonly priorityMeta = PRIORITY_META;
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly columns = [
    'select',
    'key',
    'title',
    'severity',
    'priority',
    'status',
    'assignee',
    'dueDate',
    'createdAt',
  ];

  private readonly params = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  protected readonly filters = computed(() => filtersFromParams(this.params()));
  protected readonly filterCount = computed(() => activeFilterCount(this.filters()));
  protected readonly sortState = computed(() => {
    const sort = this.filters().sort;
    return {
      active: sort.replace(/^-/, ''),
      direction: sort.startsWith('-') ? 'desc' : 'asc',
    } as const;
  });

  protected readonly cascade = rxResource({
    params: () => ({ clientId: this.filters().clientId, projectId: this.filters().projectId }),
    stream: ({ params }) => this.masters.cascade(params.clientId, params.projectId),
  });
  protected readonly sprints = rxResource({
    params: () => this.filters().projectId ?? undefined,
    stream: ({ params }) => this.sprintsApi.list(params).pipe(catchError(() => of([]))),
  });
  protected readonly users = rxResource({
    stream: () => this.masters.users().pipe(catchError(() => of([]))),
  });
  protected readonly assignees = computed(() =>
    (this.users.value() ?? []).filter((u) => u.role !== 'Tester'),
  );

  protected readonly result = rxResource({
    params: () => filtersToQuery(this.filters()),
    stream: ({ params }) => this.issuesApi.list(params),
  });
  protected readonly rows = computed(() =>
    this.result.hasValue() ? this.result.value().items : [],
  );
  protected readonly total = computed(() =>
    this.result.hasValue() ? this.result.value().totalCount : 0,
  );
  protected readonly errorMessage = computed(() => problemMessage(this.result.error()));

  protected readonly views = signal<SavedView[]>(this.savedViews.list());
  protected readonly search = signal(this.filters().q);
  private readonly searchInput = new Subject<string>();

  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly bulkAssignee = signal<number | null>(null);
  protected readonly assigning = signal(false);
  protected readonly allOnPageSelected = computed(
    () => this.rows().length > 0 && this.rows().every((r) => this.selected().has(r.key)),
  );

  constructor() {
    reloadOnIssueChanges(() => this.result.reload());
    this.searchInput
      .pipe(debounceTime(300), takeUntilDestroyed())
      .subscribe((q) => this.update({ q }));

    // Keep the search box in step with back/forward navigation.
    effect(() => {
      const q = this.filters().q;
      untracked(() => {
        if (q !== this.search().trim()) {
          this.search.set(q);
        }
      });
    });
  }

  protected readonly exporting = signal(false);

  /** The list as Excel with the current filters and sort (all pages). */
  protected exportList(): void {
    this.exporting.set(true);
    this.issuesApi.export(filtersToQuery(this.filters())).subscribe({
      next: (file) => {
        saveFile(file);
        this.exporting.set(false);
      },
      error: (error: unknown) => {
        this.exporting.set(false);
        this.toast.error(
          error instanceof HttpErrorResponse && error.status === 422
            ? 'Too many issues match – narrow the filters and export again.'
            : 'The export could not be created. Try again in a moment.',
        );
      },
    });
  }

  protected update(change: Partial<IssueFilters>): void {
    const next: IssueFilters = { ...this.filters(), page: 1, ...change };
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: filtersToParams(next),
      replaceUrl: true,
    });
  }

  protected chooseClient(clientId: number | null): void {
    this.update({ clientId, projectId: null, sprintId: null, moduleId: null });
  }

  protected chooseProject(projectId: number | null): void {
    this.update({ projectId, sprintId: null, moduleId: null });
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.searchInput.next(value);
  }

  protected clearFilters(): void {
    this.search.set('');
    this.update({ ...EMPTY_FILTERS, sort: this.filters().sort, size: this.filters().size });
  }

  protected onSort(sort: Sort): void {
    this.update({
      sort: sort.direction ? `${sort.direction === 'desc' ? '-' : ''}${sort.active}` : DEFAULT_SORT,
    });
  }

  protected onPage(event: PageEvent): void {
    this.update({ page: event.pageIndex + 1, size: event.pageSize });
  }

  protected open(row: IssueListItem): void {
    void this.router.navigate(['/issues', row.key]);
  }

  protected toggle(key: string, checked: boolean): void {
    const next = new Set(this.selected());
    if (checked) {
      next.add(key);
    } else {
      next.delete(key);
    }
    this.selected.set(next);
  }

  protected toggleAll(checked: boolean): void {
    const next = new Set(this.selected());
    for (const row of this.rows()) {
      if (checked) {
        next.add(row.key);
      } else {
        next.delete(row.key);
      }
    }
    this.selected.set(next);
  }

  protected clearSelection(): void {
    this.selected.set(new Set());
  }

  protected bulkAssign(): void {
    const userId = this.bulkAssignee();
    const keys = [...this.selected()];
    if (!userId || !keys.length || this.assigning()) {
      return;
    }
    this.assigning.set(true);
    this.issuesApi.bulkAssign(keys, userId).subscribe({
      next: (result) => {
        this.assigning.set(false);
        this.selected.set(new Set(result.failed.map((f) => f.key)));
        this.result.reload();
        const name = this.assignees().find((u) => u.id === userId)?.fullName ?? 'them';
        if (result.assigned.length) {
          this.toast.success(`${result.assigned.length} assigned to ${name}.`);
        }
        if (result.failed.length) {
          const first = result.failed[0];
          this.toast.warn(
            `${result.failed.length} not assigned – ${first.key}: ${first.reason}` +
              (result.failed.length > 1 ? ' (still selected)' : ''),
          );
        }
      },
      error: (error: unknown) => {
        this.assigning.set(false);
        this.toast.error(problemMessage(error));
      },
    });
  }

  protected applyView(view: SavedView): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: view.params,
      replaceUrl: true,
    });
  }

  protected async saveView(): Promise<void> {
    const name = await firstValueFrom(
      this.dialog
        .open<SaveViewDialog, string, string>(SaveViewDialog, { data: '', width: '400px' })
        .afterClosed(),
    );
    if (name) {
      this.views.set(this.savedViews.save(name, this.filters()));
      this.toast.success(`View “${name}” saved.`);
    }
  }

  protected removeView(view: SavedView, event: Event): void {
    event.stopPropagation();
    this.views.set(this.savedViews.remove(view.name));
  }

  protected isOverdue(row: IssueListItem): boolean {
    return (
      !!row.dueDate &&
      new Date(row.dueDate) < new Date() &&
      !['Fixed', 'Verified', 'Closed', 'Rejected'].includes(row.status)
    );
  }
}
