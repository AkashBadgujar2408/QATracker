import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheckCheck } from '@ng-icons/lucide';
import { problemMessage } from '../../core/api/api';
import { NotificationItem } from '../../core/notifications/notification-item';
import { NotificationStore } from '../../core/notifications/notification.store';
import { RealtimeService } from '../../core/realtime/realtime.service';
import {
  AppNotification,
  NotificationsApi,
  notificationLink,
} from '../../shared/data/notifications.api';
import { formatIst } from '../../shared/pipes/ist-date.pipe';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';

type Tab = 'all' | 'unread';

const PAGE_SIZE = 30;

export interface DayGroup {
  readonly label: string;
  readonly items: readonly AppNotification[];
}

/** "Today", "Yesterday", else 27/Oct/2026 – IST calendar days, newest first. */
export function groupByDay(items: readonly AppNotification[], now = new Date()): DayGroup[] {
  const today = formatIst(now);
  const yesterday = formatIst(new Date(now.getTime() - 24 * 60 * 60 * 1000));
  const groups: { label: string; items: AppNotification[] }[] = [];
  for (const item of items) {
    const day = formatIst(item.createdAt);
    const label = day === today ? 'Today' : day === yesterday ? 'Yesterday' : day;
    const last = groups.at(-1);
    if (last?.label === label) {
      last.items.push(item);
    } else {
      groups.push({ label, items: [item] });
    }
  }
  return groups;
}

/** Full notification history (ADR-028): read and unread, by day, older pages on demand, kept live. */
@Component({
  selector: 'app-notifications-page',
  imports: [PageHeader, NotificationItem, EmptyState, SkeletonLines, MatButton, NgIcon],
  viewProviders: [provideIcons({ lucideCheckCheck })],
  template: `
    <app-page-header
      title="Notifications"
      subtitle="Everything that was sent to you, newest first. Open one to go to the issue."
    >
      <button
        actions
        matButton="outlined"
        type="button"
        [disabled]="store.unreadCount() === 0"
        (click)="store.markAllRead()"
      >
        <ng-icon name="lucideCheckCheck" size="16" class="mr-1" />
        Mark all as read
      </button>
    </app-page-header>

    <div class="max-w-3xl">
      <div role="tablist" aria-label="Show" class="mb-4 flex gap-1 border-b border-border">
        @for (t of tabs; track t.id) {
          @let selected = t.id === tab();
          <button
            type="button"
            role="tab"
            [attr.aria-selected]="selected"
            class="-mb-px flex cursor-pointer items-center gap-2 border-0 border-b-2 border-solid bg-transparent px-3 py-2.5 text-base focus-visible:outline-2 focus-visible:outline-primary"
            [class.border-primary]="selected"
            [class.text-text]="selected"
            [class.font-semibold]="selected"
            [class.border-transparent]="!selected"
            [class.text-muted]="!selected"
            (click)="select(t.id)"
          >
            {{ t.label }}
            @if (t.id === 'unread' && store.unreadCount() > 0) {
              <span
                class="min-w-6 rounded-full bg-surface-muted px-1.5 text-center text-xs leading-5 font-semibold tabular-nums"
                >{{ store.unreadCount() }}</span
              >
            }
          </button>
        }
      </div>

      @if (loading() && items().length === 0) {
        <div class="rounded-card border border-border bg-surface p-4">
          <app-skeleton-lines [lines]="6" />
        </div>
      } @else if (error() && items().length === 0) {
        <app-empty-state
          kind="error"
          title="Notifications could not be loaded"
          [message]="error()!"
        >
          <button matButton="outlined" type="button" (click)="reload()">Try again</button>
        </app-empty-state>
      } @else if (items().length === 0) {
        <app-empty-state
          [title]="tab() === 'unread' ? 'You are all caught up' : 'No notifications yet'"
          message="Assignments, mentions, fixes ready to retest and reopened issues show up here."
        />
      } @else {
        @for (group of groups(); track group.label) {
          <section class="mb-5" [attr.aria-label]="group.label">
            <h2 class="m-0 mb-2 text-sm font-semibold text-muted">{{ group.label }}</h2>
            <div class="qa-group overflow-hidden rounded-card border border-border bg-surface">
              @for (n of group.items; track n.id) {
                <app-notification-item
                  [item]="n"
                  [now]="now()"
                  (opened)="open($event)"
                  (toggled)="toggle($event)"
                />
              }
            </div>
          </section>
        }
        @if (hasMore()) {
          <div class="flex justify-center pb-6">
            <button matButton="outlined" type="button" [disabled]="loading()" (click)="loadOlder()">
              {{ loading() ? 'Loading…' : 'Show older' }}
            </button>
          </div>
        }
      }
    </div>
  `,
  styles: `
    .qa-group > app-notification-item + app-notification-item {
      border-top: 1px solid var(--qa-border);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationsPage {
  protected readonly store = inject(NotificationStore);
  private readonly api = inject(NotificationsApi);
  private readonly router = inject(Router);

  protected readonly tabs: readonly { id: Tab; label: string }[] = [
    { id: 'all', label: 'All' },
    { id: 'unread', label: 'Unread' },
  ];

  protected readonly tab = signal<Tab>('all');
  protected readonly items = signal<readonly AppNotification[]>([]);
  protected readonly hasMore = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly now = signal(new Date());
  protected readonly groups = computed(() => groupByDay(this.items(), this.now()));

  constructor() {
    const realtime = inject(RealtimeService);
    const destroyRef = inject(DestroyRef);
    realtime.notificationCreated$
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe((n) =>
        this.items.update((items) => (items.some((i) => i.id === n.id) ? items : [n, ...items])),
      );
    realtime.notificationsRead$.pipe(takeUntilDestroyed(destroyRef)).subscribe(() => this.reload());
    realtime.reconnected$.pipe(takeUntilDestroyed(destroyRef)).subscribe(() => this.reload());

    // Read state changed from the bell (or here): mirror it without a reload.
    effect(() => {
      const change = this.store.readChanges();
      if (!change) {
        return;
      }
      untracked(() =>
        this.items.update((items) =>
          items.map((n) =>
            change.ids === 'all' || change.ids.includes(n.id) ? { ...n, isRead: change.read } : n,
          ),
        ),
      );
    });

    this.reload();
  }

  protected select(tab: Tab): void {
    if (tab !== this.tab()) {
      this.tab.set(tab);
      this.items.set([]);
      this.reload();
    }
  }

  protected reload(): void {
    this.load(undefined);
  }

  protected loadOlder(): void {
    this.load(this.items().at(-1)?.id);
  }

  protected open(n: AppNotification): void {
    if (!n.isRead) {
      this.store.markRead([n.id]);
    }
    const link = notificationLink(n);
    if (link) {
      void this.router.navigate([link.path], { queryParams: link.query });
    }
  }

  protected toggle(n: AppNotification): void {
    this.store.markRead([n.id], !n.isRead);
  }

  private load(before: number | undefined): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.list({ unread: this.tab() === 'unread', before, size: PAGE_SIZE }).subscribe({
      next: (page) => {
        this.loading.set(false);
        this.now.set(new Date());
        this.items.update((items) => (before ? [...items, ...page.items] : page.items));
        this.hasMore.set(page.hasMore);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(problemMessage(error, 'Notifications could not be loaded.'));
      },
    });
  }
}
