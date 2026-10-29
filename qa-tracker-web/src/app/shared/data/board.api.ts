import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/api/api';
import { IssueStatus, Priority, Severity } from '../models/issue-enums';
import { AllowedAction } from './issues.api';
import { SprintStatus } from './sprints.api';

/** A board card (GET /api/board). `moves` = allowed actions that change the status – the columns it may go to. */
export interface BoardCard {
  readonly id: number;
  readonly key: string;
  readonly title: string;
  readonly sprintId: number;
  readonly sprintName: string;
  readonly sprintStatus: SprintStatus;
  readonly moduleName: string;
  readonly screenName: string | null;
  readonly severity: Severity;
  readonly priority: Priority;
  readonly status: IssueStatus;
  readonly assigneeId: number | null;
  readonly assigneeName: string | null;
  readonly assigneeAvatarColor: string | null;
  readonly dueDate: string | null;
  readonly overdue: boolean;
  readonly reopenCount: number;
  readonly rowVersion: string;
  readonly moves: readonly AllowedAction[];
}

export interface BoardColumn {
  readonly status: IssueStatus;
  /** Every matching issue; `cards` holds at most 200. */
  readonly count: number;
  readonly cards: readonly BoardCard[];
}

export interface Board {
  readonly projectId: number;
  /** Every status in workflow order, also when empty. */
  readonly columns: readonly BoardColumn[];
}

export interface BoardQuery {
  readonly projectId: number;
  readonly sprintId?: number | null;
  readonly assigneeId?: number | null;
  readonly mine?: boolean;
  readonly unassigned?: boolean;
  readonly severity?: readonly Severity[];
  readonly q?: string;
}

@Injectable({ providedIn: 'root' })
export class BoardApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  get(query: BoardQuery): Observable<Board> {
    let params = new HttpParams().set('projectId', query.projectId);
    if (query.sprintId) {
      params = params.set('sprintId', query.sprintId);
    }
    if (query.assigneeId) {
      params = params.set('assigneeId', query.assigneeId);
    }
    if (query.mine) {
      params = params.set('mine', true);
    }
    if (query.unassigned) {
      params = params.set('unassigned', true);
    }
    for (const severity of query.severity ?? []) {
      params = params.append('severity', severity);
    }
    if (query.q?.trim()) {
      params = params.set('q', query.q.trim());
    }
    return this.http.get<Board>(`${this.base}/board`, { params });
  }
}
