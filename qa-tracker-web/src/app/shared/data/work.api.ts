import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL, SILENT_ERRORS } from '../../core/api/api';
import { IssueStatus, Priority, Severity } from '../models/issue-enums';
import { RetestResult } from './issues.api';
import { EnvironmentType } from './masters.api';

/** An issue in a work queue (My work, deploy notice candidates). */
export interface WorkItem {
  readonly id: number;
  readonly key: string;
  readonly title: string;
  readonly projectId: number;
  readonly projectKey: string;
  readonly projectName: string;
  readonly clientName: string;
  readonly sprintId: number;
  readonly sprintName: string;
  readonly moduleName: string;
  readonly screenName: string | null;
  readonly severity: Severity;
  readonly priority: Priority;
  readonly status: IssueStatus;
  readonly assigneeId: number | null;
  readonly assigneeName: string | null;
  readonly reporterName: string;
  readonly dueDate: string | null;
  readonly reopenCount: number;
  readonly createdAt: string;
  readonly updatedAt: string | null;
  readonly rowVersion: string;
}

export interface WorkQueue {
  /** Total in the tab; items hold at most the first 100, most urgent first. */
  readonly count: number;
  readonly items: readonly WorkItem[];
}

/** My work tabs (plan 8.2, ADR-026) – only sprints that completed testing. */
export interface MyWork {
  readonly assigned: WorkQueue;
  readonly available: WorkQueue;
  readonly needInfoAnswered: WorkQueue;
  readonly reopened: WorkQueue;
  readonly overdue: WorkQueue;
}

export interface RetestItem {
  readonly deploymentId: number;
  readonly issueId: number;
  readonly issueKey: string;
  readonly title: string;
  readonly moduleName: string;
  readonly screenName: string | null;
  readonly pageUrl: string | null;
  readonly severity: Severity;
  readonly priority: Priority;
  readonly reopenCount: number;
  readonly reporterId: number;
  readonly reporterName: string;
  readonly assigneeName: string | null;
  readonly deployedByName: string;
  readonly deployedAt: string;
  readonly note: string | null;
  readonly version: string | null;
  /** IssueWorkflow (Verify) for the current user. */
  readonly canRetest: boolean;
}

/** Pending retests on one environment – open the URL once, clear them all. */
export interface RetestGroup {
  readonly environmentId: number;
  readonly environmentName: string;
  readonly environmentType: EnvironmentType;
  readonly environmentUrl: string;
  readonly projectId: number;
  readonly projectKey: string;
  readonly projectName: string;
  readonly clientName: string;
  readonly oldestDeployedAt: string;
  readonly items: readonly RetestItem[];
}

export interface RetestQueue {
  readonly count: number;
  readonly groups: readonly RetestGroup[];
}

export interface CandidateDeployment {
  readonly environmentId: number;
  readonly environmentName: string;
  readonly environmentType: EnvironmentType;
  readonly retestResult: RetestResult;
  readonly deployedAt: string;
}

/** One of the caller's fixed or verified issues, with the latest deployment per environment. */
export interface DeployCandidate {
  readonly issue: WorkItem;
  readonly deployments: readonly CandidateDeployment[];
}

export interface DeployNotice {
  readonly id: number;
  readonly projectId: number;
  readonly projectKey: string;
  readonly projectName: string;
  readonly environmentId: number;
  readonly environmentName: string;
  readonly environmentType: EnvironmentType;
  readonly environmentUrl: string;
  readonly createdById: number;
  readonly createdByName: string;
  readonly createdAt: string;
  readonly note: string | null;
  readonly issues: readonly {
    readonly deploymentId: number;
    readonly issueId: number;
    readonly issueKey: string;
    readonly title: string;
    readonly version: string | null;
    readonly retestResult: RetestResult;
  }[];
}

export interface CreateDeployNotice {
  readonly environmentId: number;
  readonly issueKeys: readonly string[];
  readonly note: string;
  readonly version: string | null;
}

/** Ageing buckets of the retest queue (plan 9: < 1 day, 1–2, 3–5, > 5 days). */
export type WaitBucket = 'fresh' | 'days' | 'aging' | 'stale';

/** Whole days waited since the deployment. */
export function waitingDays(deployedAt: string, now: Date = new Date()): number {
  return Math.max(0, Math.floor((now.getTime() - new Date(deployedAt).getTime()) / 86_400_000));
}

export function waitBucket(days: number): WaitBucket {
  if (days < 1) {
    return 'fresh';
  }
  if (days <= 2) {
    return 'days';
  }
  return days <= 5 ? 'aging' : 'stale';
}

/** "Today", "1 day", "4 days". */
export function waitLabel(days: number): string {
  return days < 1 ? 'Today' : days === 1 ? '1 day' : `${days} days`;
}

const inline = { context: new HttpContext().set(SILENT_ERRORS, true) };

function optional(values: Record<string, number | boolean | null | undefined>): HttpParams {
  let params = new HttpParams();
  for (const [name, value] of Object.entries(values)) {
    if (value !== undefined && value !== null && value !== false) {
      params = params.set(name, value);
    }
  }
  return params;
}

/** Daily work screens (plan 14, ADR-026): My work, retest queue, deploy notices. */
@Injectable({ providedIn: 'root' })
export class WorkApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  myWork(projectId?: number | null): Observable<MyWork> {
    return this.http.get<MyWork>(`${this.base}/me/work`, { params: optional({ projectId }) });
  }

  retestQueue(mine = false, projectId?: number | null): Observable<RetestQueue> {
    return this.http.get<RetestQueue>(`${this.base}/retest-queue`, {
      params: optional({ projectId, mine }),
    });
  }

  deployCandidates(projectId?: number | null): Observable<DeployCandidate[]> {
    return this.http.get<DeployCandidate[]>(`${this.base}/deploy-notices/candidates`, {
      params: optional({ projectId }),
    });
  }

  deployNotices(projectId?: number | null, size = 10): Observable<DeployNotice[]> {
    return this.http.get<DeployNotice[]>(`${this.base}/deploy-notices`, {
      params: optional({ projectId, size }),
    });
  }

  createDeployNotice(notice: CreateDeployNotice): Observable<DeployNotice> {
    return this.http.post<DeployNotice>(`${this.base}/deploy-notices`, notice, inline);
  }
}
