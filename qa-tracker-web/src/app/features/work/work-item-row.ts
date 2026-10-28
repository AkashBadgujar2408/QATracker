import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideClock } from '@ng-icons/lucide';
import { WorkItem } from '../../shared/data/work.api';
import { IstDatePipe } from '../../shared/pipes/ist-date.pipe';
import { SeverityBadge } from '../../shared/ui/severity-badge/severity-badge';
import { StatusPill } from '../../shared/ui/status-pill/status-pill';

const DONE: readonly string[] = ['Fixed', 'Verified', 'Closed', 'Rejected'];

/** A due date in the past while the issue still waits for a fix. */
export function isOverdue(item: Pick<WorkItem, 'dueDate' | 'status'>, now = new Date()): boolean {
  return !!item.dueDate && new Date(item.dueDate) < now && !DONE.includes(item.status);
}

/**
 * One issue in a work queue: severity, key + title (to the detail page), where it is, status and due date.
 * `[leading]` takes a checkbox, `[actions]` the row's buttons.
 */
@Component({
  selector: 'app-work-item-row',
  imports: [RouterLink, NgIcon, IstDatePipe, SeverityBadge, StatusPill],
  viewProviders: [provideIcons({ lucideClock })],
  template: `
    <ng-content select="[leading]" />
    <div class="min-w-0 flex-1">
      <div class="flex min-w-0 items-baseline gap-2">
        <a
          class="shrink-0 font-semibold text-primary tabular-nums no-underline hover:underline"
          [routerLink]="['/issues', item().key]"
          >{{ item().key }}</a
        >
        <a
          class="min-w-0 truncate text-text no-underline hover:underline"
          [routerLink]="['/issues', item().key]"
          [title]="item().title"
          >{{ item().title }}</a
        >
      </div>
      <div class="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-muted">
        <span>{{ item().moduleName }}{{ item().screenName ? ' › ' + item().screenName : '' }}</span>
        <span>{{ item().sprintName }}</span>
        @if (item().reopenCount > 0) {
          <span class="font-medium text-status-reopened">Reopened {{ item().reopenCount }}×</span>
        }
      </div>
    </div>
    <div class="flex shrink-0 flex-wrap items-center justify-end gap-x-4 gap-y-2">
      <app-severity-badge [severity]="item().severity" />
      <app-status-pill [status]="item().status" />
      <span
        class="flex w-44 items-center gap-1 text-sm whitespace-nowrap tabular-nums"
        [class.text-muted]="!overdue()"
        [class.text-severity-critical]="overdue()"
        [class.font-medium]="overdue()"
      >
        @if (item().dueDate) {
          <ng-icon name="lucideClock" size="14" />
          {{ overdue() ? 'Overdue' : 'Due' }} {{ item().dueDate | istDate }}
        } @else {
          No due date
        }
      </span>
      <ng-content select="[actions]" />
    </div>
  `,
  host: {
    class:
      'flex flex-wrap items-center gap-x-4 gap-y-2 px-5 py-3 [&:not(:last-child)]:border-b [&:not(:last-child)]:border-border',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkItemRow {
  readonly item = input.required<WorkItem>();
  protected readonly overdue = computed(() => isOverdue(this.item()));
}
