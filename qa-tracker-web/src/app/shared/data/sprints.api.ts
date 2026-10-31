import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL, SILENT_ERRORS } from '../../core/api/api';
import { IssueStatus, Severity } from '../models/issue-enums';
import { DownloadedFile, getFile } from './download';
import { LookupItem } from './masters.api';

/** Testing sprints (plan 6, plan 14 SPRINTS, ADR-017). */
export type SprintStatus = 'Testing' | 'TestingCompleted' | 'Closed';

export const SPRINT_STATUS_LABELS: Readonly<Record<SprintStatus, string>> = {
  Testing: 'Testing',
  TestingCompleted: 'Testing completed',
  Closed: 'Closed',
};

export interface Sprint {
  readonly id: number;
  readonly projectId: number;
  readonly projectKey: string;
  readonly projectName: string;
  readonly clientId: number;
  readonly clientName: string;
  readonly name: string;
  readonly phaseId: number;
  readonly phaseName: string;
  readonly defaultEnvironmentId: number | null;
  readonly defaultEnvironmentName: string | null;
  readonly status: SprintStatus;
  readonly startedAt: string;
  readonly startedByName: string;
  readonly testingCompletedAt: string | null;
  readonly testingCompletedByName: string | null;
  readonly closedAt: string | null;
  readonly issueCount: number;
  /** Issues that are neither Closed nor Rejected. */
  readonly openIssueCount: number;
  /** Issues carried forward out of this sprint to another one. */
  readonly carriedForwardCount: number;
  readonly rowVersion: string;
}

export interface CompletionSummary {
  readonly sprintId: number;
  readonly status: SprintStatus;
  readonly issueCount: number;
  readonly unassignedCount: number;
  readonly notAssessedCount: number;
  readonly bySeverity: readonly { readonly severity: Severity; readonly count: number }[];
  readonly byModule: readonly {
    readonly moduleId: number;
    readonly moduleName: string;
    readonly count: number;
  }[];
}

export type SprintEventKind =
  | 'Started'
  | 'TestingCompleted'
  | 'ReopenedForTesting'
  | 'Closed'
  | 'IssuesCarriedForward'
  | 'IssuesCarriedIn';

/** One entry of the sprint's own history (plan 6.1). */
export interface SprintEvent {
  readonly kind: SprintEventKind;
  readonly userName: string;
  readonly note: string | null;
  readonly at: string;
}

export interface SprintDetail {
  readonly sprint: Sprint;
  readonly events: readonly SprintEvent[];
}

/** An issue that is neither Closed nor Rejected – what closing the sprint would carry forward. */
export interface OpenIssue {
  readonly id: number;
  readonly key: string;
  readonly title: string;
  readonly moduleName: string;
  readonly severity: Severity;
  readonly status: IssueStatus;
  readonly assigneeName: string | null;
  readonly rowVersion: string;
}

export interface CarryForwardResult {
  readonly sprint: Sprint;
  readonly target: Sprint;
  readonly carriedCount: number;
}

/** Issues to carry forward to another open sprint of the project, each with the version it was read at. */
export interface CarryForward {
  readonly targetSprintId: number;
  readonly issues: readonly { readonly id: number; readonly rowVersion: string }[];
}

export interface StartSprintFields {
  readonly name: string;
  readonly phaseId: number;
  readonly defaultEnvironmentId: number | null;
  readonly note?: string | null;
}

/** Dialog submissions show errors inline. */
const inline = { context: new HttpContext().set(SILENT_ERRORS, true) };

@Injectable({ providedIn: 'root' })
export class SprintsApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  /** Sprint report (GET /api/sprints/{id}/report): Excel workbook or PDF. */
  report(sprintId: number, format: 'xlsx' | 'pdf'): Observable<DownloadedFile> {
    return getFile(
      this.http,
      `${this.base}/sprints/${sprintId}/report`,
      new HttpParams().set('format', format),
      `Sprint report.${format}`,
    );
  }

  /** Newest first. */
  list(projectId: number): Observable<Sprint[]> {
    return this.http.get<Sprint[]>(`${this.base}/projects/${projectId}/sprints`);
  }

  phases(): Observable<LookupItem[]> {
    return this.http.get<LookupItem[]>(`${this.base}/lookups/phases`);
  }

  suggestedName(projectId: number, phaseId: number): Observable<{ name: string }> {
    return this.http.get<{ name: string }>(
      `${this.base}/projects/${projectId}/sprints/suggested-name`,
      { params: new HttpParams().set('phaseId', phaseId), ...inline },
    );
  }

  start(projectId: number, fields: StartSprintFields): Observable<Sprint> {
    return this.http.post<Sprint>(`${this.base}/projects/${projectId}/sprints`, fields, inline);
  }

  completionSummary(sprintId: number): Observable<CompletionSummary> {
    return this.http.get<CompletionSummary>(`${this.base}/sprints/${sprintId}/completion-summary`);
  }

  completeTesting(sprintId: number, rowVersion: string, note?: string | null): Observable<Sprint> {
    return this.http.post<Sprint>(
      `${this.base}/sprints/${sprintId}/complete-testing`,
      { rowVersion, note: note || null },
      inline,
    );
  }

  /** The sprint with its history. */
  get(sprintId: number): Observable<SprintDetail> {
    return this.http.get<SprintDetail>(`${this.base}/sprints/${sprintId}`);
  }

  openIssues(sprintId: number): Observable<OpenIssue[]> {
    return this.http.get<OpenIssue[]>(`${this.base}/sprints/${sprintId}/open-issues`);
  }

  carryForward(
    sprintId: number,
    carry: CarryForward,
    note?: string | null,
  ): Observable<CarryForwardResult> {
    return this.http.post<CarryForwardResult>(
      `${this.base}/sprints/${sprintId}/carry-forward`,
      { ...carry, note: note || null },
      inline,
    );
  }

  /** Close (422 while issues are open) – optionally carrying the open ones forward in the same request. */
  close(
    sprintId: number,
    rowVersion: string,
    note?: string | null,
    carryForward?: CarryForward | null,
  ): Observable<Sprint> {
    return this.http.post<Sprint>(
      `${this.base}/sprints/${sprintId}/close`,
      { rowVersion, note: note || null, carryForward: carryForward ?? null },
      inline,
    );
  }

  /** Admin override – the note is required. */
  reopenTesting(sprintId: number, rowVersion: string, note: string): Observable<Sprint> {
    return this.http.post<Sprint>(
      `${this.base}/sprints/${sprintId}/reopen-testing`,
      { rowVersion, note },
      inline,
    );
  }
}
