import { Injectable, inject } from '@angular/core';
import { ParamMap, Params } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { IssueQuery } from '../../shared/data/issues.api';
import {
  ISSUE_STATUSES,
  IssueStatus,
  PRIORITIES,
  Priority,
  SEVERITIES,
  Severity,
} from '../../shared/models/issue-enums';

/** Issue list state – lives in the URL so a view can be bookmarked, shared and saved. */
export interface IssueFilters {
  readonly clientId: number | null;
  readonly projectId: number | null;
  readonly sprintId: number | null;
  readonly moduleId: number | null;
  readonly status: readonly IssueStatus[];
  readonly severity: readonly Severity[];
  readonly priority: readonly Priority[];
  /** A user id, or 'none' for unassigned. */
  readonly assignee: number | 'none' | null;
  readonly overdue: boolean;
  readonly q: string;
  readonly sort: string;
  readonly page: number;
  readonly size: number;
}

export const DEFAULT_SORT = '-createdAt';
export const PAGE_SIZES = [25, 50, 100] as const;

export const EMPTY_FILTERS: IssueFilters = {
  clientId: null,
  projectId: null,
  sprintId: null,
  moduleId: null,
  status: [],
  severity: [],
  priority: [],
  assignee: null,
  overdue: false,
  q: '',
  sort: DEFAULT_SORT,
  page: 1,
  size: 25,
};

function id(value: string | null): number | null {
  const n = Number(value);
  return Number.isInteger(n) && n > 0 ? n : null;
}

function list<T extends string>(value: string | null, allowed: readonly T[]): T[] {
  return (value ?? '').split(',').filter((v): v is T => (allowed as readonly string[]).includes(v));
}

export function filtersFromParams(params: ParamMap): IssueFilters {
  const assignee = params.get('assignee');
  const size = Number(params.get('size'));
  return {
    clientId: id(params.get('client')),
    projectId: id(params.get('project')),
    sprintId: id(params.get('sprint')),
    moduleId: id(params.get('module')),
    status: list(params.get('status'), ISSUE_STATUSES),
    severity: list(params.get('severity'), SEVERITIES),
    priority: list(params.get('priority'), PRIORITIES),
    assignee: assignee === 'none' ? 'none' : id(assignee),
    overdue: params.get('overdue') === '1',
    q: params.get('q') ?? '',
    sort: params.get('sort') || DEFAULT_SORT,
    page: id(params.get('page')) ?? 1,
    size: (PAGE_SIZES as readonly number[]).includes(size) ? size : EMPTY_FILTERS.size,
  };
}

/** Query params for the router; defaults are left out so URLs stay short. */
export function filtersToParams(f: IssueFilters): Params {
  return {
    client: f.clientId,
    project: f.projectId,
    sprint: f.sprintId,
    module: f.moduleId,
    status: f.status.length ? f.status.join(',') : null,
    severity: f.severity.length ? f.severity.join(',') : null,
    priority: f.priority.length ? f.priority.join(',') : null,
    assignee: f.assignee,
    overdue: f.overdue ? '1' : null,
    q: f.q.trim() || null,
    sort: f.sort === DEFAULT_SORT ? null : f.sort,
    page: f.page > 1 ? f.page : null,
    size: f.size === EMPTY_FILTERS.size ? null : f.size,
  };
}

export function filtersToQuery(f: IssueFilters): IssueQuery {
  return {
    clientId: f.clientId ?? undefined,
    projectId: f.projectId ?? undefined,
    sprintId: f.sprintId ?? undefined,
    moduleId: f.moduleId ?? undefined,
    status: f.status,
    severity: f.severity,
    priority: f.priority,
    assigneeId: typeof f.assignee === 'number' ? f.assignee : undefined,
    unassigned: f.assignee === 'none',
    overdue: f.overdue,
    q: f.q.trim() || undefined,
    sort: f.sort,
    page: f.page,
    size: f.size,
  };
}

/** Number of filters set (for the "Clear filters" button). */
export function activeFilterCount(f: IssueFilters): number {
  return (
    [
      f.clientId,
      f.projectId,
      f.sprintId,
      f.moduleId,
      f.assignee,
      f.q.trim() || null,
      f.overdue || null,
    ].filter((v) => v !== null && v !== undefined).length +
    f.status.length +
    f.severity.length +
    f.priority.length
  );
}

export interface SavedView {
  readonly name: string;
  /** Query params without paging. */
  readonly params: Params;
}

/**
 * Saved views (plan S15 "local first"): kept in this browser per user. Storage can be unavailable – then views
 * simply are not remembered.
 */
@Injectable({ providedIn: 'root' })
export class SavedViews {
  private readonly auth = inject(AuthService);

  list(): SavedView[] {
    try {
      const raw = localStorage.getItem(this.key());
      const views = raw ? (JSON.parse(raw) as SavedView[]) : [];
      return Array.isArray(views) ? views.filter((v) => typeof v?.name === 'string') : [];
    } catch {
      return [];
    }
  }

  save(name: string, filters: IssueFilters): SavedView[] {
    const params = filtersToParams({ ...filters, page: 1 });
    const views = [...this.list().filter((v) => v.name !== name), { name, params }].sort((a, b) =>
      a.name.localeCompare(b.name),
    );
    this.write(views);
    return views;
  }

  remove(name: string): SavedView[] {
    const views = this.list().filter((v) => v.name !== name);
    this.write(views);
    return views;
  }

  private write(views: SavedView[]): void {
    try {
      localStorage.setItem(this.key(), JSON.stringify(views));
    } catch {
      // not remembered – fine
    }
  }

  private key(): string {
    return `qa.issueViews.${this.auth.user()?.id ?? 0}`;
  }
}
