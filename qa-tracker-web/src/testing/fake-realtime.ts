import { Subject } from 'rxjs';
import { signal } from '@angular/core';
import { RealtimeService } from '../app/core/realtime/realtime.service';

/** Stand-in for RealtimeService (no hub connection in tests); push events through the subjects. */
export class FakeRealtime implements Pick<
  RealtimeService,
  | 'state'
  | 'issueChanged$'
  | 'activityAdded$'
  | 'sprintChanged$'
  | 'notificationCreated$'
  | 'notificationsRead$'
  | 'reconnected$'
  | 'watchIssue'
> {
  readonly state = signal<'disconnected' | 'connecting' | 'connected' | 'reconnecting'>(
    'connected',
  );
  readonly issueChanged$ = new Subject<Parameters<RealtimeService['issueChanged$']['next']>[0]>();
  readonly activityAdded$ = new Subject<Parameters<RealtimeService['activityAdded$']['next']>[0]>();
  readonly sprintChanged$ = new Subject<Parameters<RealtimeService['sprintChanged$']['next']>[0]>();
  readonly notificationCreated$ = new Subject<
    Parameters<RealtimeService['notificationCreated$']['next']>[0]
  >();
  readonly notificationsRead$ = new Subject<{ readonly unreadCount: number }>();
  readonly reconnected$ = new Subject<void>();
  readonly watched: string[] = [];

  watchIssue(issueKey: string): () => void {
    this.watched.push(issueKey);
    return () => this.watched.splice(this.watched.indexOf(issueKey), 1);
  }
}
