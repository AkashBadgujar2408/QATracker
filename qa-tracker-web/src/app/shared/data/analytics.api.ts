import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/api/api';
import { IssueStatus, IssueType, Severity } from '../models/issue-enums';
import { RejectType } from './issues.api';
import { EnvironmentType } from './masters.api';
import { SprintStatus } from './sprints.api';

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

// Sprint dashboard (plan 9.2) – GET /api/analytics/sprints/{id}

export interface SprintHeader {
  readonly id: number;
  readonly name: string;
  readonly projectId: number;
  readonly projectKey: string;
  readonly projectName: string;
  readonly clientName: string;
  readonly phaseName: string;
  readonly status: SprintStatus;
  readonly startedAt: string;
  /** Working days from the start to testing completed (or now). */
  readonly testingDays: number;
  readonly testingCompletedAt: string | null;
  readonly closedAt: string | null;
  readonly issueCount: number;
}

export interface BurndownPoint {
  readonly date: string;
  /** Null for days still to come. */
  readonly open: number | null;
  readonly ideal: number | null;
}

export interface SprintDashboard {
  readonly sprint: SprintHeader;
  readonly progress: readonly StatusGroupCount[];
  readonly burndown: {
    readonly latestDueDate: string | null;
    readonly points: readonly BurndownPoint[];
  };
  readonly foundPerDay: readonly { readonly date: string; readonly count: number }[];
  readonly moduleSeverity: {
    readonly modules: readonly { readonly id: number; readonly name: string }[];
    readonly severities: readonly Severity[];
    readonly cells: readonly {
      readonly moduleId: number;
      readonly severity: Severity;
      readonly count: number;
    }[];
  };
  readonly byType: readonly { readonly type: IssueType; readonly count: number }[];
  readonly reopened: {
    readonly count: number;
    readonly items: readonly {
      readonly key: string;
      readonly title: string;
      readonly reopenCount: number;
      readonly status: IssueStatus;
      readonly assigneeName: string | null;
    }[];
  };
  readonly comparison: readonly {
    readonly sprintId: number;
    readonly name: string;
    readonly phaseName: string;
    readonly current: boolean;
    readonly total: number;
    readonly bySeverity: readonly SeverityCount[];
  }[];
}

// Developer dashboard (plan 9.3) – GET /api/analytics/developers

export interface Workload {
  readonly userId: number;
  readonly name: string;
  readonly avatarColor: string | null;
  /** Assigned and Reopened. */
  readonly assigned: number;
  readonly inProgress: number;
  readonly needInfo: number;
  readonly onHold: number;
}

export interface UserMetric {
  readonly userId: number;
  readonly name: string;
  readonly avatarColor: string | null;
  readonly value: number;
  /** Issues the value is based on. */
  readonly count: number;
}

export interface WeeklySeries<TKey> {
  /** Mondays (IST), yyyy-MM-dd. */
  readonly weeks: readonly string[];
  readonly series: readonly {
    readonly key: TKey;
    readonly name: string;
    readonly values: readonly number[];
  }[];
}

export interface Developers {
  readonly from: string;
  readonly to: string;
  readonly workload: readonly Workload[];
  readonly fixedPerWeek: WeeklySeries<number>;
  /** Working hours. */
  readonly averageFixHours: readonly UserMetric[];
  /** 0–1. */
  readonly firstTimeRight: readonly UserMetric[];
  readonly sla: { readonly onTime: number; readonly total: number; readonly rate: number };
  readonly me: {
    readonly open: number;
    readonly overdue: number;
    readonly fixedThisWeek: number;
    readonly averageFixHours: number | null;
  };
}

// Quality dashboard (plan 9.4) – GET /api/analytics/quality

export type AgeingBucket = 'UnderOneDay' | 'OneToTwoDays' | 'ThreeToFiveDays' | 'OverFiveDays';

export interface Quality {
  readonly from: string;
  readonly to: string;
  /** Issues per project by sprint phase (phases in sort order, only those used). */
  readonly leakage: readonly {
    readonly projectId: number;
    readonly projectKey: string;
    readonly projectName: string;
    readonly byPhase: readonly {
      readonly phaseId: number;
      readonly phase: string;
      readonly count: number;
    }[];
  }[];
  /** Deployed → retested, median working hours (retests done in the range). */
  readonly retestTurnaround: {
    readonly medianHours: number;
    readonly count: number;
    readonly byEnvironmentType: readonly {
      readonly environmentType: EnvironmentType;
      readonly medianHours: number;
      readonly count: number;
    }[];
  };
  readonly retestAgeing: readonly {
    readonly bucket: AgeingBucket;
    readonly label: string;
    readonly count: number;
  }[];
  readonly moduleHotspots: readonly {
    readonly moduleId: number;
    readonly module: string;
    readonly projectKey: string;
    readonly count: number;
    readonly open: number;
  }[];
  readonly rejected: {
    readonly rejected: number;
    readonly total: number;
    /** 0–1. */
    readonly ratio: number;
    readonly byType: readonly { readonly type: RejectType; readonly count: number }[];
  };
  readonly severityTrend: WeeklySeries<Severity>;
}

// Client health dashboard (plan 9.5) – GET /api/analytics/clients

export type Health = 'Green' | 'Amber' | 'Red';

export interface ClientCard {
  readonly clientId: number;
  readonly name: string;
  readonly open: number;
  readonly critical: number;
  readonly overdue: number;
  readonly lastSprint: {
    readonly sprintId: number;
    readonly name: string;
    readonly projectKey: string;
    readonly status: SprintStatus;
    readonly startedAt: string;
  } | null;
  readonly health: Health;
}

export interface Clients {
  readonly from: string;
  readonly to: string;
  readonly clients: readonly ClientCard[];
  readonly openBySeverity: readonly {
    readonly clientId: number;
    readonly name: string;
    readonly bySeverity: readonly SeverityCount[];
  }[];
  /** Issues per project by the type of the environment they were found on. */
  readonly uatVsLive: readonly {
    readonly projectId: number;
    readonly projectKey: string;
    readonly projectName: string;
    readonly uat: number;
    readonly live: number;
    readonly other: number;
    readonly notSet: number;
  }[];
  /** Created → closed, working days. */
  readonly averageClosureDays: readonly {
    readonly clientId: number;
    readonly name: string;
    readonly value: number;
    readonly count: number;
  }[];
}

@Injectable({ providedIn: 'root' })
export class AnalyticsApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  sprint(sprintId: number): Observable<SprintDashboard> {
    return this.http.get<SprintDashboard>(`${this.base}/analytics/sprints/${sprintId}`);
  }

  developers(query: AnalyticsQuery): Observable<Developers> {
    return this.http.get<Developers>(`${this.base}/analytics/developers`, {
      params: params(query),
    });
  }

  quality(query: AnalyticsQuery): Observable<Quality> {
    return this.http.get<Quality>(`${this.base}/analytics/quality`, { params: params(query) });
  }

  clients(query: AnalyticsQuery): Observable<Clients> {
    return this.http.get<Clients>(`${this.base}/analytics/clients`, { params: params(query) });
  }

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
