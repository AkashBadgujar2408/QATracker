import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/api/api';
import { IssueStatus, Severity } from '../models/issue-enums';

/** Dashboard scope + IST date range (yyyy-MM-dd). The API defaults to the last 30 days. */
export interface AnalyticsQuery {
  readonly clientId?: number | null;
  readonly projectId?: number | null;
  readonly sprintId?: number | null;
  readonly from?: string | null;
  readonly to?: string | null;
}

export type StatusGroup = 'Open' | 'Fixed' | 'Verified' | 'Closed' | 'Rejected';

export interface StatusGroupCount {
  readonly group: StatusGroup;
  readonly statuses: readonly IssueStatus[];
  readonly count: number;
}

export interface SeverityCount {
  readonly severity: Severity;
  readonly count: number;
}

export interface OverviewKpis {
  readonly open: number;
  readonly openWeekAgo: number;
  readonly awaitingRetest: number;
  readonly overdue: number;
  readonly criticalOpen: number;
  readonly closedThisWeek: number;
}

export interface DailyFlow {
  readonly date: string;
  readonly created: number;
  readonly closed: number;
}

export interface ProjectCount {
  readonly projectId: number;
  readonly projectKey: string;
  readonly projectName: string;
  readonly count: number;
}

export interface RecentActivity {
  readonly id: number;
  readonly kind: string;
  readonly issueKey: string;
  readonly issueTitle: string;
  readonly userName: string | null;
  readonly userAvatarColor: string | null;
  readonly summary: string;
  readonly createdAt: string;
}

/** GET /api/analytics/overview (plan 9.1, ADR-031). */
export interface Overview {
  readonly from: string;
  readonly to: string;
  readonly kpis: OverviewKpis;
  readonly statusGroups: readonly StatusGroupCount[];
  readonly openBySeverity: readonly SeverityCount[];
  readonly createdVsClosed: readonly DailyFlow[];
  readonly openByProject: readonly ProjectCount[];
  readonly recentActivity: readonly RecentActivity[];
}

@Injectable({ providedIn: 'root' })
export class AnalyticsApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  overview(query: AnalyticsQuery): Observable<Overview> {
    return this.http.get<Overview>(`${this.base}/analytics/overview`, { params: params(query) });
  }
}

function params(query: AnalyticsQuery): HttpParams {
  let p = new HttpParams();
  for (const [name, value] of Object.entries(query)) {
    if (value !== null && value !== undefined && value !== '') {
      p = p.set(name, value as string | number);
    }
  }
  return p;
}
