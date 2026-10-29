import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL, SILENT_ERRORS } from '../../core/api/api';

/** Notification events (plan 8.3). */
export type NotificationType =
  | 'TestingCompleted'
  | 'Assigned'
  | 'Mentioned'
  | 'Fixed'
  | 'Reopened'
  | 'NeedInfo'
  | 'Disputed'
  | 'Comment'
  | 'CriticalUnassigned'
  | 'Overdue';

export interface AppNotification {
  readonly id: number;
  readonly type: NotificationType;
  readonly title: string;
  readonly body: string | null;
  readonly issueKey: string | null;
  readonly sprintId: number | null;
  readonly projectId: number | null;
  readonly isRead: boolean;
  readonly createdAt: string;
}

export interface NotificationPage {
  readonly items: readonly AppNotification[];
  readonly unreadCount: number;
  readonly hasMore: boolean;
}

/** The signed-in user's notification history (ADR-028): read and unread, newest first. */
@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  list(
    options: { unread?: boolean; before?: number; size?: number } = {},
  ): Observable<NotificationPage> {
    let params = new HttpParams();
    if (options.unread) {
      params = params.set('unread', true);
    }
    if (options.before) {
      params = params.set('before', options.before);
    }
    if (options.size) {
      params = params.set('size', options.size);
    }
    // Screens show their own error state (no global toast).
    return this.http.get<NotificationPage>(`${this.baseUrl}/notifications`, {
      params,
      context: new HttpContext().set(SILENT_ERRORS, true),
    });
  }

  unreadCount(): Observable<{ unreadCount: number }> {
    return this.http.get<{ unreadCount: number }>(`${this.baseUrl}/notifications/unread-count`);
  }

  /** Mark read (or unread with `read: false`); returns the new unread count. */
  mark(body: {
    ids?: readonly number[];
    all?: boolean;
    read?: boolean;
  }): Observable<{ unreadCount: number }> {
    return this.http.post<{ unreadCount: number }>(`${this.baseUrl}/notifications/read`, body);
  }
}

/** Where a notification leads: the issue, else the sprint's issues. */
export function notificationLink(
  n: AppNotification,
): { path: string; query?: Record<string, number> } | null {
  if (n.issueKey) {
    return { path: `/issues/${n.issueKey}` };
  }
  if (n.projectId) {
    return {
      path: '/issues',
      query: n.sprintId ? { project: n.projectId, sprint: n.sprintId } : { project: n.projectId },
    };
  }
  return null;
}
