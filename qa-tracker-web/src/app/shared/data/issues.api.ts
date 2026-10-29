import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL, SILENT_ERRORS } from '../../core/api/api';
import { IssueStatus, IssueType, Priority, Severity } from '../models/issue-enums';
import { SprintStatus } from './sprints.api';

/** Issues (plan 7.1, plan 14 ISSUES, ADR-018). Rich-text fields are sanitised HTML. */
export interface Issue {
  readonly id: number;
  readonly key: string;
  readonly number: number;
  readonly clientId: number;
  readonly clientName: string;
  readonly projectId: number;
  readonly projectKey: string;
  readonly projectName: string;
  readonly sprintId: number;
  readonly sprintName: string;
  readonly sprintStatus: SprintStatus;
  readonly moduleId: number;
  readonly moduleName: string;
  readonly screenName: string | null;
  readonly foundOnEnvironmentId: number | null;
  readonly foundOnEnvironmentName: string | null;
  readonly pageUrl: string | null;
  readonly type: IssueType;
  readonly title: string;
  readonly steps: string | null;
  readonly expectedResult: string | null;
  readonly actualResult: string;
  readonly impact: string | null;
  readonly severity: Severity;
  readonly priority: Priority;
  readonly status: IssueStatus;
  readonly reporterId: number;
  readonly reporterName: string;
  readonly assigneeId: number | null;
  readonly assigneeName: string | null;
  readonly dueDate: string | null;
  readonly reopenCount: number;
  readonly createdAt: string;
  readonly updatedAt: string | null;
  readonly rowVersion: string;
  readonly permissions: { readonly canEdit: boolean; readonly canDelete: boolean };
  /** Deployments of the fix, oldest first (environment strip, retests). */
  readonly deployments: readonly Deployment[];
  /** Workflow actions the current user may take now (IssueWorkflow on the API – never repeat the rules here). */
  readonly allowedActions: readonly AllowedAction[];
}

export type RetestResult = 'Pending' | 'Passed' | 'Failed';

export interface Deployment {
  readonly id: number;
  readonly environmentId: number;
  readonly environmentName: string;
  readonly environmentType: 'UAT' | 'LIVE' | 'Other';
  readonly environmentUrl: string;
  readonly deployedByName: string;
  readonly deployedAt: string;
  readonly note: string | null;
  readonly version: string | null;
  readonly retestResult: RetestResult;
  readonly retestedByName: string | null;
  readonly retestedAt: string | null;
  readonly retestComment: string | null;
}

export type RejectType = 'Invalid' | 'Duplicate' | 'AsDesigned' | 'WontFix';

export const REJECT_TYPE_LABELS: Readonly<Record<RejectType, string>> = {
  Invalid: 'Invalid',
  Duplicate: 'Duplicate',
  AsDesigned: 'As designed',
  WontFix: "Won't fix",
};

export interface RelatedIssue {
  readonly id: number;
  readonly key: string;
  readonly title: string;
  readonly screenName: string | null;
  readonly severity: Severity;
  readonly status: IssueStatus;
  readonly assigneeName: string | null;
  readonly sameScreen: boolean;
}

export interface BulkAssignResult {
  readonly assigned: readonly string[];
  readonly failed: readonly {
    readonly key: string;
    readonly code: string;
    readonly reason: string;
  }[];
}

export interface TransitionInput {
  readonly reason?: string | null;
  readonly rejectType?: RejectType | null;
  readonly duplicateOfKey?: string | null;
}

export type IssueAction =
  | 'Assign'
  | 'Pick'
  | 'Start'
  | 'NeedInfo'
  | 'Resume'
  | 'Hold'
  | 'Unhold'
  | 'Dispute'
  | 'Reject'
  | 'Contest'
  | 'MarkFixed'
  | 'Verify'
  | 'Reopen'
  | 'Close';

export interface AllowedAction {
  readonly action: IssueAction;
  readonly target: IssueStatus;
  /** What the user must provide: 'assignee' | 'reason' | 'rejectType' | 'environments'. */
  readonly requires: readonly ('assignee' | 'reason' | 'rejectType' | 'environments')[];
}

export interface IssueListItem {
  readonly id: number;
  readonly key: string;
  readonly title: string;
  readonly projectId: number;
  readonly projectKey: string;
  readonly sprintId: number;
  readonly sprintName: string;
  readonly moduleName: string;
  readonly screenName: string | null;
  readonly type: IssueType;
  readonly severity: Severity;
  readonly priority: Priority;
  readonly status: IssueStatus;
  readonly assigneeId: number | null;
  readonly assigneeName: string | null;
  readonly reporterName: string;
  readonly dueDate: string | null;
  readonly createdAt: string;
  readonly updatedAt: string | null;
}

export interface PagedResult<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

/** Fields of the issue form, shared by create and update. */
export interface IssueFields {
  readonly moduleId: number;
  readonly screenName: string | null;
  readonly foundOnEnvironmentId: number | null;
  readonly pageUrl: string | null;
  readonly type: IssueType;
  readonly title: string;
  readonly steps: string | null;
  readonly expectedResult: string | null;
  readonly actualResult: string;
  readonly impact: string | null;
  readonly severity: Severity;
  readonly priority: Priority;
  readonly assigneeId: number | null;
}

export interface IssueQuery {
  readonly clientId?: number;
  readonly projectId?: number;
  readonly sprintId?: number;
  readonly moduleId?: number;
  readonly envId?: number;
  readonly status?: readonly IssueStatus[];
  readonly severity?: readonly Severity[];
  readonly priority?: readonly Priority[];
  readonly assigneeId?: number;
  readonly unassigned?: boolean;
  readonly overdue?: boolean;
  readonly q?: string;
  readonly page?: number;
  readonly size?: number;
  /** Field name, "-field" for descending. */
  readonly sort?: string;
}

/** What a workflow call needs: the key and the version it was read at (an Issue, a board card or a work item). */
export type IssueRef = Pick<Issue, 'key' | 'rowVersion'>;

/** The API allows at most 200 rows per page. */
export const MAX_PAGE_SIZE = 200;

/** Form submissions show errors inline. */
const inline = { context: new HttpContext().set(SILENT_ERRORS, true) };

@Injectable({ providedIn: 'root' })
export class IssuesApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  list(query: IssueQuery): Observable<PagedResult<IssueListItem>> {
    let params = new HttpParams();
    for (const [name, value] of Object.entries(query)) {
      if (Array.isArray(value)) {
        for (const item of value as readonly string[]) {
          params = params.append(name, item); // repeated: status=New&status=Fixed
        }
      } else if (value !== undefined && value !== null && value !== '' && value !== false) {
        params = params.set(name, value as string | number | boolean);
      }
    }
    return this.http.get<PagedResult<IssueListItem>>(`${this.base}/issues`, { params });
  }

  get(key: string): Observable<Issue> {
    return this.http.get<Issue>(`${this.base}/issues/${encodeURIComponent(key)}`);
  }

  create(sprintId: number, fields: IssueFields): Observable<Issue> {
    return this.http.post<Issue>(`${this.base}/issues`, { sprintId, ...fields }, inline);
  }

  /** Optimistic concurrency: a stale rowVersion answers 409. */
  update(key: string, rowVersion: string, fields: IssueFields): Observable<Issue> {
    return this.http.put<Issue>(`${this.base}/issues/${encodeURIComponent(key)}`, fields, {
      ...inline,
      headers: { 'If-Match': `"${rowVersion}"` },
    });
  }

  delete(key: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/issues/${encodeURIComponent(key)}`);
  }

  /** Screen names used in the project – the module's first, most used first. */
  screens(projectId: number, moduleId?: number | null, q?: string): Observable<string[]> {
    let params = new HttpParams();
    if (moduleId) {
      params = params.set('moduleId', moduleId);
    }
    if (q) {
      params = params.set('q', q);
    }
    return this.http.get<string[]>(`${this.base}/projects/${projectId}/screens`, { params });
  }

  // Workflow (plan 14, ADR-023). Single-issue actions send the issue's version; a stale one answers 409.

  assign(issue: IssueRef, userId: number, note?: string | null): Observable<Issue> {
    return this.post(issue, 'assign', { userId, note: note || null });
  }

  pick(key: string): Observable<Issue> {
    return this.http.post<Issue>(`${this.issueUrl(key)}/pick`, {}, inline);
  }

  transition(issue: IssueRef, action: IssueAction, input: TransitionInput = {}): Observable<Issue> {
    return this.post(issue, 'transition', { action, ...input });
  }

  markFixed(
    issue: IssueRef,
    note: string,
    environmentIds: readonly number[],
    version?: string | null,
  ): Observable<Issue> {
    return this.post(issue, 'fixed', { note, environmentIds, version: version || null });
  }

  retest(deploymentId: number, passed: boolean, comment?: string | null): Observable<Issue> {
    return this.http.post<Issue>(
      `${this.base}/deployments/${deploymentId}/retest`,
      { passed, comment: comment || null },
      inline,
    );
  }

  bulkAssign(
    keys: readonly string[],
    userId: number,
    note?: string | null,
  ): Observable<BulkAssignResult> {
    return this.http.post<BulkAssignResult>(
      `${this.base}/issues/bulk-assign`,
      { keys, userId, note: note || null },
      inline,
    );
  }

  related(key: string): Observable<RelatedIssue[]> {
    return this.http.get<RelatedIssue[]>(`${this.issueUrl(key)}/related`);
  }

  private post(issue: IssueRef, action: string, body: object): Observable<Issue> {
    return this.http.post<Issue>(`${this.issueUrl(issue.key)}/${action}`, body, {
      ...inline,
      headers: { 'If-Match': `"${issue.rowVersion}"` },
    });
  }

  private issueUrl(key: string): string {
    return `${this.base}/issues/${encodeURIComponent(key)}`;
  }
}
