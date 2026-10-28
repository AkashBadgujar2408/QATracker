import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
  model,
  output,
} from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { MatTooltip } from '@angular/material/tooltip';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCopy, lucidePencil, lucideTrash2 } from '@ng-icons/lucide';
import { IssueListItem } from '../../shared/data/issues.api';
import { IstDatePipe } from '../../shared/pipes/ist-date.pipe';
import { PluralPipe } from '../../shared/pipes/plural.pipe';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PriorityIcon } from '../../shared/ui/priority-icon/priority-icon';
import { SeverityBadge } from '../../shared/ui/severity-badge/severity-badge';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';
import { StatusPill } from '../../shared/ui/status-pill/status-pill';
import { UserAvatar } from '../../shared/ui/user-avatar/user-avatar';

interface IssueGroup {
  readonly name: string;
  readonly issues: readonly IssueListItem[];
}

/** Right panel of the Testing workspace: this sprint's issues, newest first, optionally grouped by module. */
@Component({
  selector: 'app-sprint-issues',
  imports: [
    MatButton,
    MatIconButton,
    MatSlideToggle,
    MatTooltip,
    NgIcon,
    IstDatePipe,
    PluralPipe,
    EmptyState,
    PriorityIcon,
    SeverityBadge,
    SkeletonLines,
    StatusPill,
    UserAvatar,
  ],
  viewProviders: [provideIcons({ lucideCopy, lucidePencil, lucideTrash2 })],
  template: `
    <div class="flex items-center gap-3 border-b border-border px-5 py-3">
      <h2 class="m-0 flex-1 text-md font-semibold">
        This sprint's issues
        @if (issues().length) {
          <span class="ml-1 font-normal text-muted tabular-nums">{{ issues().length }}</span>
        }
      </h2>
      <mat-slide-toggle [checked]="grouped()" (change)="grouped.set($event.checked)"
        >Group by module</mat-slide-toggle
      >
    </div>

    @if (loading() && !issues().length) {
      <app-skeleton-lines [lines]="6" class="block p-5" />
    } @else if (error()) {
      <app-empty-state kind="error" title="Could not load the issues" [message]="error()!">
        <button matButton="outlined" type="button" (click)="retry.emit()">Try again</button>
      </app-empty-state>
    } @else if (!issues().length) {
      <app-empty-state
        title="No issues logged yet"
        [message]="
          readOnly()
            ? 'This sprint has no issues.'
            : 'Fill in the form and press Ctrl+Enter – each issue appears here at once.'
        "
      />
    } @else {
      <div class="max-h-[calc(100vh-15rem)] overflow-y-auto">
        @for (group of groups(); track group.name) {
          @if (grouped()) {
            <h3
              class="sticky top-0 z-[1] m-0 flex items-center gap-2 border-b border-border bg-surface-muted px-5 py-1.5 text-sm font-semibold text-text"
            >
              <span class="min-w-0 flex-1 truncate">{{ group.name }}</span>
              <span class="font-normal text-muted">{{
                group.issues.length | plural: 'issue'
              }}</span>
            </h3>
          }
          <ul class="m-0 list-none p-0">
            @for (issue of group.issues; track issue.id) {
              @let active = issue.key === activeKey();
              <li
                class="qa-issue-row group relative flex items-start gap-3 border-b border-border px-5 py-3 last:border-b-0"
                [class.bg-primary/5]="active"
              >
                @if (active) {
                  <span
                    class="absolute inset-y-0 left-0 w-[3px] bg-primary"
                    aria-hidden="true"
                  ></span>
                }
                <div class="min-w-0 flex-1">
                  <div class="flex items-center gap-2">
                    <span class="text-sm font-semibold text-primary tabular-nums">{{
                      issue.key
                    }}</span>
                    <app-severity-badge [severity]="issue.severity" />
                    <app-priority-icon [priority]="issue.priority" />
                    @if (issue.status !== 'New' && issue.status !== 'Assigned') {
                      <app-status-pill [status]="issue.status" />
                    }
                  </div>
                  <div class="mt-0.5 line-clamp-2 text-base text-text" [title]="issue.title">
                    {{ issue.title }}
                  </div>
                  <div class="mt-0.5 truncate text-sm text-muted">
                    {{ where(issue) }} · {{ issue.type }} ·
                    <span class="whitespace-nowrap">{{
                      issue.createdAt | istDate: 'datetime'
                    }}</span>
                  </div>
                </div>
                <div class="flex shrink-0 flex-col items-end gap-1">
                  @if (issue.assigneeName) {
                    <app-user-avatar
                      [name]="issue.assigneeName"
                      size="sm"
                      [matTooltip]="'Assigned to ' + issue.assigneeName"
                    />
                  } @else {
                    <span class="text-xs text-muted">Unassigned</span>
                  }
                  @if (!readOnly()) {
                    <div
                      class="qa-row-actions flex items-center opacity-0 group-focus-within:opacity-100 group-hover:opacity-100"
                    >
                      <button
                        matIconButton
                        type="button"
                        [attr.aria-label]="'Edit ' + issue.key"
                        matTooltip="Edit"
                        (click)="edit.emit(issue)"
                      >
                        <ng-icon name="lucidePencil" size="16" />
                      </button>
                      <button
                        matIconButton
                        type="button"
                        [attr.aria-label]="'New issue from ' + issue.key"
                        matTooltip="Clone"
                        (click)="clone.emit(issue)"
                      >
                        <ng-icon name="lucideCopy" size="16" />
                      </button>
                      <button
                        matIconButton
                        type="button"
                        class="text-severity-critical"
                        [attr.aria-label]="'Delete ' + issue.key"
                        matTooltip="Delete"
                        (click)="remove.emit(issue)"
                      >
                        <ng-icon name="lucideTrash2" size="16" />
                      </button>
                    </div>
                  }
                </div>
              </li>
            }
          </ul>
        }
      </div>
    }
  `,
  styles: `
    .qa-row-actions button {
      --mat-icon-button-state-layer-size: 32px;
      width: 32px;
      height: 32px;
      padding: 0;
    }
    @media (hover: none) {
      .qa-row-actions {
        opacity: 1;
      }
    }
  `,
  host: { class: 'block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SprintIssues {
  /** Newest first. */
  readonly issues = input<readonly IssueListItem[]>([]);
  readonly loading = input(false, { transform: booleanAttribute });
  readonly error = input<string | null>(null);
  readonly readOnly = input(false, { transform: booleanAttribute });
  /** Key of the issue open in the form. */
  readonly activeKey = input<string | null>(null);
  readonly grouped = model(false);

  readonly edit = output<IssueListItem>();
  readonly clone = output<IssueListItem>();
  readonly remove = output<IssueListItem>();
  readonly retry = output<void>();

  /** "Module › Screen"; grouped rows already sit under their module. */
  protected where(issue: IssueListItem): string {
    const parts = this.grouped() ? [issue.screenName] : [issue.moduleName, issue.screenName];
    return parts.filter(Boolean).join(' › ') || 'No screen';
  }

  protected readonly groups = computed<readonly IssueGroup[]>(() => {
    const issues = this.issues();
    if (!this.grouped()) {
      return [{ name: '', issues }];
    }
    const byModule = new Map<string, IssueListItem[]>();
    for (const issue of issues) {
      const list = byModule.get(issue.moduleName) ?? [];
      list.push(issue);
      byModule.set(issue.moduleName, list);
    }
    return [...byModule.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([name, list]) => ({ name, issues: list }));
  });
}
