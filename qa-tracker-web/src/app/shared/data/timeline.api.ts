import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL, SILENT_ERRORS } from '../../core/api/api';
import { Attachment } from './attachments.api';

/** Issue timeline (plan 7.5, ADR-024). */
export type ActivityKind =
  | 'Created'
  | 'Comment'
  | 'StatusChanged'
  | 'Assigned'
  | 'FieldChanged'
  | 'Deployed'
  | 'RetestPassed'
  | 'RetestFailed'
  | 'Attachment'
  | 'CarriedForward'
  | 'SprintEvent';

export type TimelineFilter = 'All' | 'Comments' | 'History';

export interface Mention {
  readonly userId: number;
  readonly userName: string;
  readonly fullName: string;
}

export interface Activity {
  readonly id: number;
  readonly kind: ActivityKind;
  readonly userId: number | null;
  readonly userName: string | null;
  readonly avatarColor: string | null;
  /** Ready-made line for events ("Akash assigned to Nitin"); null for comments. */
  readonly summary: string | null;
  /** Comment text (plain text with @mentions) or the event's note/reason. */
  readonly body: string | null;
  readonly oldValue: string | null;
  readonly newValue: string | null;
  readonly environmentId: number | null;
  readonly environmentName: string | null;
  readonly deploymentId: number | null;
  readonly createdAt: string;
  readonly editedAt: string | null;
  readonly canEdit: boolean;
  readonly mentions: readonly Mention[];
  readonly attachments: readonly Attachment[];
}

/** Oldest first; `hasMore` = older entries exist (ask with before = items[0].id). */
export interface TimelinePage {
  readonly items: readonly Activity[];
  readonly hasMore: boolean;
}

/** Comment errors are shown in the composer. */
const inline = { context: new HttpContext().set(SILENT_ERRORS, true) };

@Injectable({ providedIn: 'root' })
export class TimelineApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  activities(
    key: string,
    filter: TimelineFilter = 'All',
    before?: number,
    size = 50,
  ): Observable<TimelinePage> {
    let params = new HttpParams().set('filter', filter).set('size', size);
    if (before) {
      params = params.set('before', before);
    }
    return this.http.get<TimelinePage>(
      `${this.base}/issues/${encodeURIComponent(key)}/activities`,
      {
        params,
      },
    );
  }

  comment(key: string, body: string, files: readonly File[] = []): Observable<Activity> {
    const form = new FormData();
    if (body.trim()) {
      form.append('body', body);
    }
    for (const file of files) {
      form.append('files', file, file.name);
    }
    return this.http.post<Activity>(
      `${this.base}/issues/${encodeURIComponent(key)}/comments`,
      form,
      inline,
    );
  }

  editComment(id: number, body: string): Observable<Activity> {
    return this.http.put<Activity>(`${this.base}/comments/${id}`, { body }, inline);
  }
}
