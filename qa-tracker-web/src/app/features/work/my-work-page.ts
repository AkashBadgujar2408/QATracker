import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { problemMessage, problemOf } from '../../core/api/api';
import { ToastService } from '../../core/feedback/toast.service';
import { reloadOnIssueChanges } from '../../core/realtime/realtime.service';
import { IssuesApi } from '../../shared/data/issues.api';
import { MyWork, WorkApi, WorkItem } from '../../shared/data/work.api';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';
import { WorkItemRow } from './work-item-row';

export type WorkTab = 'assigned' | 'available' | 'need-info' | 'reopened' | 'overdue';

interface TabDef {
  readonly id: WorkTab;
  readonly label: string;
  readonly queue: keyof MyWork;
  /** Shown when the tab has nothing in it. */
  readonly empty: string;
}

export const WORK_TABS: readonly TabDef[] = [
  {
    id: 'assigned',
    label: 'Assigned to me',
    queue: 'assigned',
    empty: 'Nothing is waiting for a fix from you. Pick an issue from Available to pick.',
  },
  {
    id: 'available',
    label: 'Available to pick',
    queue: 'available',
    empty: 'Every open issue in sprints that completed testing has someone on it.',
  },
  {
    id: 'need-info',
    label: 'Need info answered',
    queue: 'needInfoAnswered',
    empty: 'No answers yet to the questions you asked.',
  },
  {
    id: 'reopened',
    label: 'Reopened',
    queue: 'reopened',
    empty: 'None of your fixes failed a retest.',
  },
  {
    id: 'overdue',
    label: 'Overdue',
    queue: 'overdue',
    empty: 'You are within every due date.',
  },
];

export function tabFrom(value: string | null): WorkTab {
  return WORK_TABS.some((t) => t.id === value) ? (value as WorkTab) : 'assigned';
}

/** My work (plan 8.2): the developer's tabs with counts; Available to pick has a one-click Pick. */
@Component({
  selector: 'app-my-work-page',
  imports: [MatButton, RouterLink, EmptyState, PageHeader, SkeletonLines, WorkItemRow],
  templateUrl: './my-work-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyWorkPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly workApi = inject(WorkApi);
  private readonly issues = inject(IssuesApi);
  private readonly toast = inject(ToastService);

  protected readonly tabs = WORK_TABS;
  protected readonly tab = toSignal(
    this.route.queryParamMap.pipe(map((p) => tabFrom(p.get('tab')))),
    { initialValue: tabFrom(this.route.snapshot.queryParamMap.get('tab')) },
  );

  protected readonly work = rxResource({ stream: () => this.workApi.myWork() });

  constructor() {
    reloadOnIssueChanges(() => this.work.reload());
  }
  protected readonly current = computed(() => WORK_TABS.find((t) => t.id === this.tab())!);
  protected readonly queue = computed(() =>
    this.work.hasValue() ? this.work.value()[this.current().queue] : null,
  );
  protected readonly picking = signal<string | null>(null);

  protected count(tab: TabDef): number | null {
    return this.work.hasValue() ? this.work.value()[tab.queue].count : null;
  }

  protected select(tab: WorkTab): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab: tab === 'assigned' ? null : tab },
      replaceUrl: true,
    });
  }

  /** Arrow keys move between tabs (WAI-ARIA tabs pattern). */
  protected onTabKey(event: KeyboardEvent): void {
    const index = WORK_TABS.findIndex((t) => t.id === this.tab());
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
    if (!step) {
      return;
    }
    event.preventDefault();
    const next = WORK_TABS[(index + step + WORK_TABS.length) % WORK_TABS.length];
    this.select(next.id);
    document.getElementById(`work-tab-${next.id}`)?.focus();
  }

  protected pick(item: WorkItem): void {
    if (this.picking()) {
      return;
    }
    this.picking.set(item.key);
    this.issues.pick(item.key).subscribe({
      next: () => {
        this.picking.set(null);
        this.toast.success(`You picked ${item.key} – it is in progress.`);
        this.work.reload();
      },
      error: (error: unknown) => {
        this.picking.set(null);
        this.toast.error(
          problemOf(error)?.status === 409
            ? problemMessage(error)
            : `Could not pick ${item.key}: ${problemMessage(error)}`,
        );
        this.work.reload();
      },
    });
  }
}
