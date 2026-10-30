import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideClock, lucideEllipsisVertical, lucideRotateCcw } from '@ng-icons/lucide';
import { BoardCard as Card } from '../../shared/data/board.api';
import { AllowedAction } from '../../shared/data/issues.api';
import { ISSUE_STATUS_META, SEVERITY_META } from '../../shared/models/issue-enums';
import { IstDatePipe } from '../../shared/pipes/ist-date.pipe';
import { PriorityIcon } from '../../shared/ui/priority-icon/priority-icon';
import { UserAvatar } from '../../shared/ui/user-avatar/user-avatar';
import { OverdueBadge } from '../../shared/ui/overdue-badge/overdue-badge';

/**
 * One issue on the board: severity edge, key + title, where it is, due date, reopen count, assignee. The menu offers
 * the same moves as dragging (keyboard and touch friendly).
 */
@Component({
  selector: 'app-board-card',
  imports: [
    MatIconButton,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    RouterLink,
    NgIcon,
    IstDatePipe,
    OverdueBadge,
    PriorityIcon,
    UserAvatar,
  ],
  viewProviders: [provideIcons({ lucideClock, lucideEllipsisVertical, lucideRotateCcw })],
  template: `
    <div class="flex items-start gap-2">
      <a
        class="min-w-0 flex-1 text-text no-underline"
        [routerLink]="['/issues', card().key]"
        [attr.aria-label]="card().key + ': ' + card().title"
      >
        <span class="flex items-center gap-2 text-sm">
          <span class="font-semibold text-primary tabular-nums">{{ card().key }}</span>
          <span class="truncate text-xs text-muted">{{ severityLabel() }}</span>
        </span>
        <span class="qa-clamp mt-1 block leading-snug">{{ card().title }}</span>
      </a>
      @if (card().moves.length) {
        <button
          matIconButton
          type="button"
          class="qa-card-menu -mt-1 -mr-2 shrink-0"
          [matMenuTriggerFor]="moves"
          [attr.aria-label]="'Move ' + card().key"
          (mousedown)="$event.stopPropagation()"
        >
          <ng-icon name="lucideEllipsisVertical" size="16" />
        </button>
        <mat-menu #moves="matMenu" xPosition="before">
          <p class="m-0 px-4 pt-1 pb-2 text-xs text-muted">Move to</p>
          @for (m of card().moves; track m.action) {
            <button mat-menu-item type="button" (click)="move.emit(m)">
              <span class="flex items-center gap-2">
                <span class="qa-dot" [style.background]="statusColor(m)"></span>
                {{ statusLabel(m) }}
              </span>
            </button>
          }
        </mat-menu>
      }
    </div>
    <p class="m-0 mt-1 truncate text-sm text-muted" [title]="where()">{{ where() }}</p>
    <div class="mt-2 flex items-center gap-3 text-sm">
      <app-priority-icon [priority]="card().priority" />
      @if (card().dueDate; as due) {
        @if (card().overdue) {
          <app-overdue-badge [dueDate]="due" />
        } @else {
          <span class="inline-flex items-center gap-1 text-muted tabular-nums" title="Due date">
            <ng-icon name="lucideClock" size="14" />
            {{ due | istDate }}
          </span>
        }
      }
      @if (card().reopenCount > 0) {
        <span
          class="inline-flex items-center gap-1 font-medium text-status-reopened"
          [title]="'Reopened ' + card().reopenCount + ' times'"
        >
          <ng-icon name="lucideRotateCcw" size="14" />{{ card().reopenCount }}
        </span>
      }
      <span class="ml-auto">
        @if (card().assigneeName) {
          <app-user-avatar
            [name]="card().assigneeName!"
            [color]="card().assigneeAvatarColor"
            size="sm"
          />
        } @else {
          <span class="qa-nobody" title="Unassigned" aria-label="Unassigned" role="img"></span>
        }
      </span>
    </div>
  `,
  styles: `
    :host {
      display: block;
      position: relative;
      padding: 10px 12px 10px 15px;
      border: 1px solid var(--qa-border);
      border-radius: var(--qa-radius-card);
      background: var(--qa-surface);
      cursor: grab;
      transition:
        border-color 120ms,
        box-shadow 120ms;
    }
    :host::before {
      content: '';
      position: absolute;
      inset: 8px auto 8px 0;
      width: 3px;
      border-radius: 0 3px 3px 0;
      background: var(--severity-color);
    }
    :host(:hover) {
      border-color: var(--qa-border-strong);
    }
    :host:has(a:focus-visible) {
      outline: 2px solid var(--qa-primary);
      outline-offset: 2px;
    }
    a:focus-visible {
      outline: none;
    }
    .qa-clamp {
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }
    .qa-nobody {
      display: inline-block;
      width: 24px;
      height: 24px;
      border-radius: 50%;
      border: 1.5px dashed var(--qa-border-strong);
      vertical-align: middle;
    }
    .qa-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
    }
    .qa-card-menu {
      --mat-icon-button-state-layer-size: 32px;
      width: 32px;
      height: 32px;
      padding: 0;
      display: inline-flex;
      align-items: center;
      justify-content: center;
    }
  `,
  host: {
    '[style.--severity-color]': 'severityColor()',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BoardCardView {
  readonly card = input.required<Card>();
  /** A move chosen from the menu. */
  readonly move = output<AllowedAction>();

  protected readonly severityLabel = computed(() => SEVERITY_META[this.card().severity].label);
  protected readonly severityColor = computed(
    () => `var(${SEVERITY_META[this.card().severity].colorToken})`,
  );
  protected readonly where = computed(() => {
    const c = this.card();
    return `${c.moduleName}${c.screenName ? ' › ' + c.screenName : ''}`;
  });

  protected statusLabel(move: AllowedAction): string {
    return ISSUE_STATUS_META[move.target].label;
  }

  protected statusColor(move: AllowedAction): string {
    return `var(${ISSUE_STATUS_META[move.target].colorToken})`;
  }
}
