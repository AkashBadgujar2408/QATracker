import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { Params, Router } from '@angular/router';
import { problemMessage } from '../../core/api/api';
import { AuthService } from '../../core/auth/auth.service';
import { reloadOnIssueChanges } from '../../core/realtime/realtime.service';
import {
  ChartCategory,
  ChartItem,
  ChartSelection,
  ChartSeries,
} from '../../shared/charts/chart-options';
import { ChartBar, ChartGauge, ChartLine, ChartStackedBar } from '../../shared/charts/charts';
import {
  DashboardFilterBar,
  DashboardFilters,
  analyticsQuery,
  injectDashboardFilters,
  scopeParams,
} from '../../shared/charts/dashboard-filters';
import { KpiCard } from '../../shared/charts/kpi-card';
import { AnalyticsApi, Developers, UserMetric } from '../../shared/data/analytics.api';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { OPEN_STATUSES, shortDay } from './overview-page';

/** Workload parts (plan 9.3) and the statuses each opens in the issue list. */
export const WORKLOAD_PARTS = [
  {
    key: 'assigned',
    name: 'Assigned',
    color: '--qa-status-assigned',
    statuses: 'Assigned,Reopened',
  },
  {
    key: 'inProgress',
    name: 'In Progress',
    color: '--qa-status-in-progress',
    statuses: 'InProgress',
  },
  { key: 'needInfo', name: 'Need Info', color: '--qa-status-need-info', statuses: 'NeedInfo' },
  { key: 'onHold', name: 'On Hold', color: '--qa-status-on-hold', statuses: 'OnHold' },
] as const;

/** Lines for at most this many developers; the rest are summed as "Others" (no generated colours). */
export const MAX_LINES = 5;

export function workload(d: Developers): { categories: ChartCategory[]; series: ChartSeries[] } {
  return {
    categories: d.workload.map((w) => ({ key: String(w.userId), label: w.name })),
    series: WORKLOAD_PARTS.map((p) => ({
      key: p.key,
      name: p.name,
      color: p.color,
      values: d.workload.map((w) => w[p.key]),
    })),
  };
}

/** Fixes per week: the busiest developers get a categorical colour each, everyone else is "Others". */
export function fixedPerWeek(d: Developers): {
  categories: ChartCategory[];
  series: ChartSeries[];
} {
  const lines = d.fixedPerWeek.series;
  const top = lines.slice(0, MAX_LINES);
  const rest = lines.slice(MAX_LINES);
  const series: ChartSeries[] = top.map((l, i) => ({
    key: String(l.key),
    name: l.name,
    color: `--qa-chart-${i + 1}`,
    values: l.values,
  }));
  if (rest.length) {
    series.push({
      key: 'others',
      name: `Others (${rest.length})`,
      color: '--qa-text-muted',
      values: d.fixedPerWeek.weeks.map((_, w) => rest.reduce((sum, l) => sum + l.values[w], 0)),
    });
  }
  return {
    categories: d.fixedPerWeek.weeks.map((w) => ({ key: w, label: `Week of ${shortDay(w)}` })),
    series,
  };
}

export function metricItems(metrics: readonly UserMetric[], scale = 1): ChartItem[] {
  return metrics.map((m) => ({
    key: String(m.userId),
    label: `${m.name} (${m.count})`,
    value: Math.round(m.value * scale * 10) / 10,
  }));
}

/** "12.5 h" – working hours. */
export const hours = (value: number): string => `${value} h`;
export const percent = (value: number): string => `${value}%`;

export function workloadClickThrough(selection: ChartSelection, filters: DashboardFilters): Params {
  const part = WORKLOAD_PARTS.find((p) => p.key === selection.series);
  return { ...scopeParams(filters), assignee: selection.key, status: part?.statuses };
}

/**
 * Developer dashboard (plan 9.3): team workload, fixes per week, average fix time, first-time-right, SLA compliance,
 * and the signed-in developer's own numbers ("My stats").
 */
@Component({
  selector: 'app-developers-page',
  imports: [
    MatButton,
    ChartBar,
    ChartGauge,
    ChartLine,
    ChartStackedBar,
    DashboardFilterBar,
    KpiCard,
    EmptyState,
    PageHeader,
  ],
  templateUrl: './developers-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DevelopersPage {
  private readonly api = inject(AnalyticsApi);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  protected readonly filters = injectDashboardFilters();
  protected readonly scope = computed(() => scopeParams(this.filters()));
  protected readonly openStatuses = OPEN_STATUSES;
  protected readonly hours = hours;
  protected readonly percent = percent;
  /** Testers fix nothing – "My stats" is for developers and admins. */
  protected readonly showMyStats = computed(() => this.auth.hasRole('Developer', 'Admin'));
  protected readonly me = computed(() => this.auth.user()?.id ?? null);

  protected readonly developers = rxResource({
    params: () => analyticsQuery(this.filters()),
    stream: ({ params }) => this.api.developers(params),
  });
  protected readonly data = computed(() =>
    this.developers.hasValue() ? this.developers.value() : null,
  );
  protected readonly loading = computed(() => this.developers.isLoading() && !this.data());
  protected readonly error = computed(() =>
    this.developers.error() ? problemMessage(this.developers.error()) : null,
  );

  protected readonly workload = computed(() => {
    const d = this.data();
    return d ? workload(d) : { categories: [], series: [] };
  });
  protected readonly fixed = computed(() => {
    const d = this.data();
    return d ? fixedPerWeek(d) : { categories: [], series: [] };
  });
  protected readonly fixHours = computed(() => metricItems(this.data()?.averageFixHours ?? []));
  protected readonly firstTimeRight = computed(() =>
    metricItems(this.data()?.firstTimeRight ?? [], 100),
  );
  protected readonly myAverage = computed(() => {
    const value = this.data()?.me.averageFixHours;
    return value === null || value === undefined ? '–' : hours(value);
  });
  /** Both metric charts share a height so their row lines up. */
  protected readonly metricHeight = computed(() =>
    Math.max(200, Math.max(this.fixHours().length, this.firstTimeRight().length) * 40 + 60),
  );
  protected readonly workloadHeight = computed(() =>
    Math.max(200, this.workload().categories.length * 40 + 80),
  );

  constructor() {
    reloadOnIssueChanges(() => this.developers.reload(), undefined, 1500);
  }

  protected openWorkload(selection: ChartSelection): void {
    void this.router.navigate(['/issues'], {
      queryParams: workloadClickThrough(selection, this.filters()),
    });
  }
}
