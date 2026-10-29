import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatTooltip } from '@angular/material/tooltip';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideAtSign,
  lucideCircleHelp,
  lucideClockAlert,
  lucideFlag,
  lucideMessageSquare,
  lucideMessageSquareWarning,
  lucideRocket,
  lucideRotateCcw,
  lucideSiren,
  lucideUserPlus,
} from '@ng-icons/lucide';
import { AppNotification, NotificationType } from '../../shared/data/notifications.api';
import { formatIst, relativeTime } from '../../shared/pipes/ist-date.pipe';

/** Icons of every notification type, for components that show them. */
export const NOTIFICATION_ICONS = {
  lucideAtSign,
  lucideCircleHelp,
  lucideClockAlert,
  lucideFlag,
  lucideMessageSquare,
  lucideMessageSquareWarning,
  lucideRocket,
  lucideRotateCcw,
  lucideSiren,
  lucideUserPlus,
};

interface TypeMeta {
  readonly icon: string;
  /** Token the icon chip and unread rail are tinted with. */
  readonly tone: string;
  readonly label: string;
}

/** Each event keeps the colour of the state it is about (status / severity tokens). */
export const NOTIFICATION_META: Readonly<Record<NotificationType, TypeMeta>> = {
  TestingCompleted: {
    icon: 'lucideFlag',
    tone: '--qa-sprint-testing-completed',
    label: 'Testing completed',
  },
  Assigned: { icon: 'lucideUserPlus', tone: '--qa-status-assigned', label: 'Assigned to you' },
  Mentioned: { icon: 'lucideAtSign', tone: '--qa-primary', label: 'Mention' },
  Fixed: { icon: 'lucideRocket', tone: '--qa-status-fixed', label: 'Ready to retest' },
  Reopened: { icon: 'lucideRotateCcw', tone: '--qa-status-reopened', label: 'Reopened' },
  NeedInfo: { icon: 'lucideCircleHelp', tone: '--qa-status-need-info', label: 'Needs information' },
  Disputed: { icon: 'lucideMessageSquareWarning', tone: '--qa-status-disputed', label: 'Disputed' },
  Comment: { icon: 'lucideMessageSquare', tone: '--qa-text-muted', label: 'Comment' },
  CriticalUnassigned: {
    icon: 'lucideSiren',
    tone: '--qa-severity-critical',
    label: 'Critical unassigned',
  },
  Overdue: { icon: 'lucideClockAlert', tone: '--qa-severity-high', label: 'Overdue' },
};

/**
 * One notification: tinted type icon, title, two lines of detail, issue key and time; unread ones carry a rail in the
 * type's colour and a bold title. The row opens it; the dot on the right toggles read / unread.
 */
@Component({
  selector: 'app-notification-item',
  imports: [NgIcon, MatTooltip],
  viewProviders: [provideIcons(NOTIFICATION_ICONS)],
  template: `
    <div
      class="qa-note group relative flex gap-3 py-3 pl-4 pr-2"
      [class.qa-note-unread]="!item().isRead"
      [style.--tone]="toneVar()"
    >
      <button
        type="button"
        class="qa-note-open absolute inset-0 cursor-pointer border-0 bg-transparent p-0"
        [attr.aria-label]="(item().isRead ? '' : 'Unread: ') + item().title"
        (click)="opened.emit(item())"
      ></button>
      <span
        class="qa-note-icon pointer-events-none relative mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-full"
        [attr.title]="meta().label"
      >
        <ng-icon [name]="meta().icon" size="16" aria-hidden="true" />
      </span>
      <div class="pointer-events-none relative min-w-0 flex-1">
        <p class="m-0 text-sm leading-snug text-text" [class.font-semibold]="!item().isRead">
          {{ item().title }}
        </p>
        @if (item().body) {
          <p class="qa-note-body m-0 mt-0.5 text-sm leading-snug text-muted">{{ item().body }}</p>
        }
        <p class="m-0 mt-1 flex items-center gap-2 text-xs text-muted">
          @if (item().issueKey) {
            <span class="rounded-control bg-surface-muted px-1.5 py-px font-medium text-text">
              {{ item().issueKey }}
            </span>
          }
          <time [attr.datetime]="item().createdAt" [attr.title]="exact()">{{ ago() }}</time>
        </p>
      </div>
      <button
        type="button"
        class="qa-note-toggle relative flex size-8 shrink-0 cursor-pointer items-center justify-center self-start rounded-full border-0 bg-transparent text-muted"
        [attr.aria-label]="item().isRead ? 'Mark as unread' : 'Mark as read'"
        [matTooltip]="item().isRead ? 'Mark as unread' : 'Mark as read'"
        (click)="toggled.emit(item())"
      >
        <span class="qa-note-dot block size-2.5 rounded-full"></span>
      </button>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .qa-note {
      transition: background-color 120ms ease;
    }
    .qa-note:hover {
      background: var(--qa-surface-muted);
    }
    .qa-note:has(.qa-note-open:focus-visible) {
      outline: 2px solid var(--qa-focus-ring);
      outline-offset: -2px;
    }
    .qa-note-unread::before {
      content: '';
      position: absolute;
      left: 0;
      top: 8px;
      bottom: 8px;
      width: 3px;
      border-radius: 0 3px 3px 0;
      background: var(--tone);
    }
    .qa-note-icon {
      color: var(--tone);
      background: color-mix(in srgb, var(--tone) 14%, transparent);
    }
    .qa-note-body {
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
      overflow-wrap: anywhere;
    }
    .qa-note-toggle:hover,
    .qa-note-toggle:focus-visible {
      background: color-mix(in srgb, var(--qa-text) 8%, transparent);
    }
    .qa-note-toggle:focus-visible {
      outline: 2px solid var(--qa-focus-ring);
      outline-offset: -2px;
    }
    .qa-note-dot {
      border: 1.5px solid var(--qa-border-strong);
      opacity: 0;
      transition: opacity 120ms ease;
    }
    .qa-note:hover .qa-note-dot,
    .qa-note-toggle:focus-visible .qa-note-dot {
      opacity: 1;
    }
    .qa-note-unread .qa-note-dot {
      opacity: 1;
      border-color: var(--qa-primary);
      background: var(--qa-primary);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationItem {
  readonly item = input.required<AppNotification>();
  /** Re-evaluates the relative time when it changes. */
  readonly now = input<Date>(new Date());

  readonly opened = output<AppNotification>();
  readonly toggled = output<AppNotification>();

  protected readonly meta = computed(() => NOTIFICATION_META[this.item().type]);
  protected readonly toneVar = computed(() => `var(${this.meta().tone})`);
  protected readonly ago = computed(() => relativeTime(this.item().createdAt, this.now()));
  protected readonly exact = computed(() => formatIst(this.item().createdAt, 'datetime'));
}
