import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { CdkConnectedOverlay, CdkOverlayOrigin, ConnectedPosition } from '@angular/cdk/overlay';
import { Router, RouterLink } from '@angular/router';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatTooltip } from '@angular/material/tooltip';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBell, lucideBellOff, lucideCheckCheck } from '@ng-icons/lucide';
import { AppNotification, notificationLink } from '../../shared/data/notifications.api';
import { NotificationItem } from './notification-item';
import { NotificationStore } from './notification.store';

type BellTab = 'all' | 'unread';

const POSITIONS: ConnectedPosition[] = [
  { originX: 'end', originY: 'bottom', overlayX: 'end', overlayY: 'top', offsetY: 8 },
  { originX: 'center', originY: 'bottom', overlayX: 'center', overlayY: 'top', offsetY: 8 },
];

/**
 * Top-bar bell (plan 8.3, ADR-028): unread badge, a panel with the latest notifications (All / Unread), mark one or all
 * read, and a link to the full history. Nudges once when a notification arrives live.
 */
@Component({
  selector: 'app-notification-bell',
  imports: [
    CdkConnectedOverlay,
    CdkOverlayOrigin,
    MatIconButton,
    MatButton,
    MatTooltip,
    NgIcon,
    NotificationItem,
    RouterLink,
  ],
  viewProviders: [provideIcons({ lucideBell, lucideBellOff, lucideCheckCheck })],
  template: `
    <button
      matIconButton
      type="button"
      cdkOverlayOrigin
      #origin="cdkOverlayOrigin"
      class="qa-bell"
      [class.qa-bell-nudge]="nudging()"
      [attr.aria-label]="ariaLabel()"
      [attr.aria-expanded]="open()"
      aria-haspopup="dialog"
      matTooltip="Notifications"
      [matTooltipDisabled]="open()"
      (click)="toggle()"
      (animationend)="nudging.set(false)"
    >
      <ng-icon name="lucideBell" size="20" />
      @if (store.badge(); as badge) {
        <span class="qa-bell-badge" aria-hidden="true">{{ badge }}</span>
      }
    </button>

    <ng-template
      cdkConnectedOverlay
      [cdkConnectedOverlayOrigin]="origin"
      [cdkConnectedOverlayOpen]="open()"
      [cdkConnectedOverlayPositions]="positions"
      [cdkConnectedOverlayHasBackdrop]="true"
      cdkConnectedOverlayBackdropClass="cdk-overlay-transparent-backdrop"
      (backdropClick)="open.set(false)"
      (overlayKeydown)="onKeydown($event)"
      (detach)="open.set(false)"
    >
      <section
        class="qa-bell-panel flex flex-col overflow-hidden rounded-card border border-border bg-surface shadow-dialog"
        role="dialog"
        aria-label="Notifications"
      >
        <header class="flex items-center gap-2 border-b border-border px-4 pb-0 pt-3">
          <div class="flex flex-1 flex-col">
            <div class="flex items-center justify-between">
              <h2 class="m-0 text-md font-semibold text-text">Notifications</h2>
              <button
                matButton
                type="button"
                class="qa-compact"
                [disabled]="store.unreadCount() === 0"
                (click)="store.markAllRead()"
              >
                <ng-icon name="lucideCheckCheck" size="16" class="mr-1" />
                Mark all as read
              </button>
            </div>
            <div class="mt-1 flex gap-4" role="tablist" aria-label="Show">
              @for (t of tabs; track t.id) {
                <button
                  type="button"
                  role="tab"
                  class="qa-bell-tab"
                  [attr.aria-selected]="tab() === t.id"
                  (click)="tab.set(t.id)"
                >
                  {{ t.label }}
                  @if (t.id === 'unread' && store.unreadCount() > 0) {
                    <span class="ml-1 text-xs text-muted">{{ store.unreadCount() }}</span>
                  }
                </button>
              }
            </div>
          </div>
        </header>

        <div class="qa-bell-list min-h-0 flex-1 overflow-y-auto" role="tabpanel">
          @if (store.loading() && store.recent().length === 0) {
            @for (row of [1, 2, 3]; track row) {
              <div class="flex gap-3 px-4 py-3" aria-hidden="true">
                <div class="qa-skel size-8 rounded-full"></div>
                <div class="flex flex-1 flex-col gap-2">
                  <div class="qa-skel h-3.5 w-3/4 rounded"></div>
                  <div class="qa-skel h-3 w-1/2 rounded"></div>
                </div>
              </div>
            }
          } @else if (store.error() && store.recent().length === 0) {
            <div class="px-6 py-8 text-center text-sm text-muted">
              <p class="m-0">{{ store.error() }}</p>
              <button matButton type="button" class="mt-2" (click)="store.load()">Try again</button>
            </div>
          } @else if (shown().length === 0) {
            <div class="flex flex-col items-center px-6 py-10 text-center">
              <ng-icon name="lucideBellOff" size="28" class="text-muted" />
              <p class="m-0 mt-3 text-sm font-medium text-text">
                {{ tab() === 'unread' ? 'You are all caught up' : 'No notifications yet' }}
              </p>
              <p class="m-0 mt-1 text-sm text-muted">
                Assignments, mentions, fixes and reopened issues show up here.
              </p>
            </div>
          } @else {
            @for (n of shown(); track n.id) {
              <app-notification-item
                [item]="n"
                [now]="now()"
                (opened)="openItem($event)"
                (toggled)="store.markRead([$event.id], !$event.isRead)"
              />
            }
          }
        </div>

        <footer class="border-t border-border px-2 py-1.5">
          <a matButton routerLink="/notifications" class="w-full" (click)="open.set(false)">
            See all notifications
          </a>
        </footer>
      </section>
    </ng-template>
  `,
  styles: `
    :host {
      display: inline-flex;
    }
    .qa-bell {
      position: relative;
    }
    .qa-bell-badge {
      position: absolute;
      /* Sits on the bell's top-right shoulder, leaving the bell itself readable. */
      top: 3px;
      left: 50%;
      margin-left: 3px;
      min-width: 17px;
      height: 17px;
      padding: 0 4px;
      border-radius: var(--qa-radius-pill);
      background: var(--qa-severity-critical);
      color: var(--qa-danger-contrast);
      font-size: 11px;
      font-weight: 700;
      line-height: 17px;
      text-align: center;
      box-shadow: 0 0 0 2px var(--qa-surface);
      pointer-events: none;
    }
    .qa-bell-nudge ng-icon {
      animation: qa-bell-nudge 700ms ease-in-out;
      transform-origin: 50% 10%;
    }
    @keyframes qa-bell-nudge {
      0%,
      100% {
        transform: rotate(0);
      }
      20% {
        transform: rotate(14deg);
      }
      40% {
        transform: rotate(-12deg);
      }
      60% {
        transform: rotate(8deg);
      }
      80% {
        transform: rotate(-4deg);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      .qa-bell-nudge ng-icon {
        animation: none;
      }
    }
    .qa-bell-panel {
      width: min(420px, calc(100vw - 24px));
      max-height: min(600px, calc(100vh - var(--qa-topbar-height) - 24px));
    }
    .qa-bell-tab {
      position: relative;
      padding: 8px 0 10px;
      border: 0;
      background: none;
      color: var(--qa-text-muted);
      font: inherit;
      font-size: var(--qa-text-sm);
      font-weight: 500;
      cursor: pointer;
    }
    .qa-bell-tab[aria-selected='true'] {
      color: var(--qa-text);
    }
    .qa-bell-tab[aria-selected='true']::after {
      content: '';
      position: absolute;
      left: 0;
      right: 0;
      bottom: -1px;
      height: 2px;
      border-radius: 2px;
      background: var(--qa-primary);
    }
    .qa-bell-tab:focus-visible {
      outline: 2px solid var(--qa-focus-ring);
      outline-offset: 2px;
    }
    .qa-bell-list > app-notification-item + app-notification-item {
      border-top: 1px solid var(--qa-border);
    }
    .qa-skel {
      background: var(--qa-surface-muted);
    }
    .qa-compact {
      --mat-button-text-container-height: 32px;
      font-size: var(--qa-text-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationBell {
  protected readonly store = inject(NotificationStore);
  private readonly router = inject(Router);

  protected readonly positions = POSITIONS;
  protected readonly tabs: readonly { id: BellTab; label: string }[] = [
    { id: 'all', label: 'All' },
    { id: 'unread', label: 'Unread' },
  ];

  protected readonly open = signal(false);
  protected readonly tab = signal<BellTab>('all');
  protected readonly nudging = signal(false);
  protected readonly now = signal(new Date());

  protected readonly shown = computed(() =>
    this.tab() === 'unread' ? this.store.recent().filter((n) => !n.isRead) : this.store.recent(),
  );
  protected readonly ariaLabel = computed(() => {
    const count = this.store.unreadCount();
    return count === 0 ? 'Notifications' : `Notifications, ${count} unread`;
  });

  constructor() {
    let first = true;
    effect(() => {
      this.store.arrivals();
      if (first) {
        first = false;
        return;
      }
      untracked(() => this.nudging.set(true));
    });
  }

  protected toggle(): void {
    if (!this.open()) {
      this.now.set(new Date());
    }
    this.open.update((o) => !o);
  }

  protected openItem(n: AppNotification): void {
    if (!n.isRead) {
      this.store.markRead([n.id]);
    }
    this.open.set(false);
    const link = notificationLink(n);
    if (link) {
      void this.router.navigate([link.path], { queryParams: link.query });
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.open.set(false);
    }
  }
}
