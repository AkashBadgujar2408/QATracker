import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { AuthService } from '../auth/auth.service';
import { RealtimeService } from '../realtime/realtime.service';
import { AppNotification, NotificationsApi } from '../../shared/data/notifications.api';
import { problemMessage } from '../api/api';

const RECENT_SIZE = 15;

/**
 * The bell (ADR-028): unread count and the most recent notifications, kept live from the hub. The full history is
 * paged from the API by the notifications page; marking read here and there stays in sync (also across tabs).
 */
@Injectable({ providedIn: 'root' })
export class NotificationStore {
  private readonly api = inject(NotificationsApi);
  private readonly auth = inject(AuthService);
  private readonly realtime = inject(RealtimeService);

  readonly unreadCount = signal(0);
  readonly recent = signal<readonly AppNotification[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  /** Bumps on every live notification (the bell's nudge). */
  readonly arrivals = signal(0);
  /** Changes of read state made here, for pages showing their own copy of the history. */
  readonly readChanges = signal<{ ids: readonly number[] | 'all'; read: boolean } | null>(null);

  readonly badge = computed(() => {
    const count = this.unreadCount();
    return count === 0 ? null : count > 99 ? '99+' : String(count);
  });

  constructor() {
    effect(() => {
      const signedIn = this.auth.isAuthenticated() && !this.auth.mustChangePassword();
      untracked(() => (signedIn ? this.load() : this.clear()));
    });
    this.realtime.notificationCreated$.subscribe((n) => {
      this.recent.update((items) =>
        items.some((i) => i.id === n.id) ? items : [n, ...items].slice(0, RECENT_SIZE),
      );
      this.unreadCount.update((c) => c + (n.isRead ? 0 : 1));
      this.arrivals.update((a) => a + 1);
    });
    this.realtime.notificationsRead$.subscribe(({ unreadCount }) => {
      this.unreadCount.set(unreadCount);
      this.load(); // another tab changed read state – refresh which ones
    });
    this.realtime.reconnected$.subscribe(() => this.load());
  }

  load(): void {
    this.loading.set(true);
    this.api.list({ size: RECENT_SIZE }).subscribe({
      next: (page) => {
        this.loading.set(false);
        this.error.set(null);
        this.recent.set(page.items);
        this.unreadCount.set(page.unreadCount);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(problemMessage(error, 'Notifications could not be loaded.'));
      },
    });
  }

  markRead(ids: readonly number[], read = true): void {
    if (ids.length === 0) {
      return;
    }
    const set = new Set(ids);
    this.apply((n) => set.has(n.id), read);
    this.readChanges.set({ ids, read });
    this.api.mark({ ids, read }).subscribe({
      next: ({ unreadCount }) => this.unreadCount.set(unreadCount),
      error: () => this.rollback(),
    });
  }

  markAllRead(): void {
    this.apply(() => true, true);
    this.unreadCount.set(0);
    this.readChanges.set({ ids: 'all', read: true });
    this.api.mark({ all: true }).subscribe({
      next: ({ unreadCount }) => this.unreadCount.set(unreadCount),
      error: () => this.rollback(),
    });
  }

  private apply(match: (n: AppNotification) => boolean, read: boolean): void {
    let delta = 0;
    this.recent.update((items) =>
      items.map((n) => {
        if (!match(n) || n.isRead === read) {
          return n;
        }
        delta += read ? -1 : 1;
        return { ...n, isRead: read };
      }),
    );
    this.unreadCount.update((c) => Math.max(0, c + delta));
  }

  /** The error toast comes from the HTTP interceptor; show the server's state again. */
  private rollback(): void {
    this.load();
  }

  private clear(): void {
    this.recent.set([]);
    this.unreadCount.set(0);
    this.error.set(null);
  }
}
