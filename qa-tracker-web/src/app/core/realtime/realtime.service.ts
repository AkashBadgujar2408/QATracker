import { DestroyRef, Injectable, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { Observable, Subject, debounceTime, filter, firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { AppNotification } from '../../shared/data/notifications.api';
import { ActivityKind } from '../../shared/data/timeline.api';
import { SprintStatus } from '../../shared/data/sprints.api';

/** Hub path; proxied to the API in development (proxy.conf.json, `ws: true`). */
export const HUB_URL = '/hubs/tracker';

export interface IssueChanged {
  readonly issueKey: string;
  readonly projectId: number;
  readonly sprintId: number;
  readonly deleted: boolean;
}

export interface ActivityAdded {
  readonly issueKey: string;
  readonly activityId: number;
  readonly kind: ActivityKind;
}

export interface SprintChanged {
  readonly sprintId: number;
  readonly projectId: number;
  readonly status: SprintStatus;
}

export type RealtimeState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/**
 * Live updates from `/hubs/tracker` (plan 14, ADR-028). Connects while signed in, reconnects on its own and re-joins
 * the issue groups of open issue pages. Events are hints – screens reload what they show from the API.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly auth = inject(AuthService);

  private connection: HubConnection | null = null;
  /** Issue pages open in this tab (key → number of watchers). */
  private readonly watched = new Map<string, number>();

  readonly state = signal<RealtimeState>('disconnected');

  readonly issueChanged$ = new Subject<IssueChanged>();
  readonly activityAdded$ = new Subject<ActivityAdded>();
  readonly sprintChanged$ = new Subject<SprintChanged>();
  readonly notificationCreated$ = new Subject<AppNotification>();
  readonly notificationsRead$ = new Subject<{ readonly unreadCount: number }>();
  /** After a reconnect: events may have been missed, screens should reload. */
  readonly reconnected$ = new Subject<void>();

  constructor() {
    effect(() => {
      const signedIn = this.auth.isAuthenticated() && !this.auth.mustChangePassword();
      untracked(() => (signedIn ? void this.start() : void this.stop()));
    });
  }

  /** Join the issue's group while its page is open; call the returned function on leave. */
  watchIssue(issueKey: string): () => void {
    const key = issueKey.trim().toUpperCase();
    const count = this.watched.get(key) ?? 0;
    this.watched.set(key, count + 1);
    if (count === 0) {
      this.invoke('WatchIssue', key);
    }
    return () => {
      const left = (this.watched.get(key) ?? 1) - 1;
      if (left > 0) {
        this.watched.set(key, left);
        return;
      }
      this.watched.delete(key);
      this.invoke('UnwatchIssue', key);
    };
  }

  private async start(): Promise<void> {
    if (this.connection) {
      return;
    }
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL, { accessTokenFactory: () => this.freshToken() })
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000, 60_000])
      .configureLogging(LogLevel.Warning)
      .build();
    this.connection = connection;

    connection.on('IssueChanged', (e: IssueChanged) => this.issueChanged$.next(e));
    connection.on('ActivityAdded', (e: ActivityAdded) => this.activityAdded$.next(e));
    connection.on('SprintChanged', (e: SprintChanged) => this.sprintChanged$.next(e));
    connection.on('NotificationCreated', (n: AppNotification) => this.notificationCreated$.next(n));
    connection.on('NotificationsRead', (e: { unreadCount: number }) =>
      this.notificationsRead$.next(e),
    );
    connection.onreconnecting(() => this.state.set('reconnecting'));
    connection.onreconnected(() => {
      this.state.set('connected');
      this.rejoin();
      this.reconnected$.next();
    });
    connection.onclose(() => {
      this.state.set('disconnected');
      if (this.connection === connection) {
        this.connection = null;
        // Gave up reconnecting (e.g. server restart): try again a little later while still signed in.
        setTimeout(() => {
          if (this.auth.isAuthenticated()) {
            void this.start().then(() => this.reconnected$.next());
          }
        }, 30_000);
      }
    });

    this.state.set('connecting');
    try {
      await connection.start();
      this.state.set('connected');
      this.rejoin();
    } catch {
      // onclose does not fire for a failed start: retry the same way.
      this.state.set('disconnected');
      if (this.connection === connection) {
        this.connection = null;
        setTimeout(() => {
          if (this.auth.isAuthenticated()) {
            void this.start();
          }
        }, 15_000);
      }
    }
  }

  private async stop(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    this.watched.clear();
    if (connection) {
      await connection.stop().catch(() => undefined);
    }
    this.state.set('disconnected');
  }

  private rejoin(): void {
    for (const key of this.watched.keys()) {
      this.invoke('WatchIssue', key);
    }
  }

  private invoke(method: string, key: string): void {
    if (this.connection?.state === HubConnectionState.Connected) {
      this.connection.invoke(method, key).catch(() => undefined);
    }
  }

  /** The access token lives in memory and expires: refresh it first when it is about to. */
  private async freshToken(): Promise<string> {
    const token = this.auth.accessToken();
    if (token && !expiresSoon(token)) {
      return token;
    }
    await firstValueFrom(this.auth.refresh());
    return this.auth.accessToken() ?? '';
  }
}

function expiresSoon(token: string): boolean {
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/'))) as {
      exp?: number;
    };
    return !payload.exp || payload.exp * 1000 - Date.now() < 30_000;
  } catch {
    return true;
  }
}

/**
 * Calls `reload` (debounced) whenever an issue changes – optionally only matching ones – or the hub reconnects.
 * Use in an injection context; stops with the component.
 */
export function reloadOnIssueChanges(
  reload: () => void,
  matches: (e: IssueChanged) => boolean = () => true,
  wait = 400,
): void {
  const realtime = inject(RealtimeService);
  const destroyRef = inject(DestroyRef);
  const changes = new Observable<void>((subscriber) => {
    const a = realtime.issueChanged$.pipe(filter(matches)).subscribe(() => subscriber.next());
    const b = realtime.reconnected$.subscribe(() => subscriber.next());
    return () => {
      a.unsubscribe();
      b.unsubscribe();
    };
  });
  changes.pipe(debounceTime(wait), takeUntilDestroyed(destroyRef)).subscribe(() => reload());
}
