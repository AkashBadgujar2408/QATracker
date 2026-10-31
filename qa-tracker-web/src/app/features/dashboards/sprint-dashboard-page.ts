import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { ActivatedRoute, Params, Router, RouterLink } from '@angular/router';
import { catchError, of } from 'rxjs';
import { problemMessage } from '../../core/api/api';
import { reloadOnIssueChanges } from '../../core/realtime/realtime.service';
import {
  ChartCategory,
  ChartItem,
  ChartSelection,
  ChartSeries,
  HeatmapData,
} from '../../shared/charts/chart-options';
import { ChartBar, ChartHeatmap, ChartLine, ChartStackedBar } from '../../shared/charts/charts';
import {
  DashboardFilterBar,
  DashboardFilters,
  dashboardFiltersToParams,
  injectDashboardFilters,
  scopeParams,
} from '../../shared/charts/dashboard-filters';
import { KpiCard } from '../../shared/charts/kpi-card';
import { AnalyticsApi, SprintDashboard } from '../../shared/data/analytics.api';
import { Sprint, SprintsApi } from '../../shared/data/sprints.api';
import { SEVERITY_META, Severity } from '../../shared/models/issue-enums';
import { IstDatePipe } from '../../shared/pipes/ist-date.pipe';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { SprintReportButton } from '../../shared/reports/sprint-report-button';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { StatusPill } from '../../shared/ui/status-pill/status-pill';
import { SprintStatusBadge } from '../../shared/ui/sprint-status/sprint-status';
import { GROUP_META, shortDay } from './overview-page';

/** The sprint to show when the URL names a project but no sprint: the newest one. */
export function latestSprint(sprints: readonly Sprint[]): Sprint | null {
  return [...sprints].sort((a, b) => b.startedAt.localeCompare(a.startedAt))[0] ?? null;
}

/** Progress (plan 9.2): one 100 % bar of the status groups. */
export function progressSeries(d: SprintDashboard): ChartSeries[] {
  return d.progress.map((g) => ({
    key: g.group,
    name: GROUP_META[g.group].label,
    color: GROUP_META[g.group].color,
    values: [g.count],
  }));
}

/** Burn-down: open issues per day since testing completed and the ideal line to the latest due date. */
export function burndown(d: SprintDashboard): {
  categories: ChartCategory[];
  series: ChartSeries[];
} {
  const points = d.burndown.points;
  return {
    categories: points.map((p) => ({ key: p.date, label: shortDay(p.date) })),
    series: [
      { key: 'open', name: 'Open', color: '--qa-primary', values: points.map((p) => p.open) },
      {
        key: 'ideal',
        name: 'Ideal',
        color: '--qa-text-muted',
        values: points.map((p) => p.ideal),
        dashed: true,
      },
    ],
  };
}

export function heatmap(d: SprintDashboard): HeatmapData {
  const m = d.moduleSeverity;
  const used = m.severities.filter((s) => m.cells.some((c) => c.severity === s && c.count > 0));
  return {
    rows: m.modules.map((mod) => ({ key: String(mod.id), label: mod.name })),
    columns: used.map((s) => ({ key: s, label: SEVERITY_META[s].label })),
    values: m.modules.map((mod) =>
      used.map((s) => m.cells.find((c) => c.moduleId === mod.id && c.severity === s)?.count ?? 0),
    ),
    color: '--qa-primary',
  };
}

/** Sprint comparison (plan 9.2): issues per sprint of the project, stacked by severity. */
export function comparison(d: SprintDashboard): {
  categories: ChartCategory[];
  series: ChartSeries[];
} {
  const severities: Severity[] = ['Critical', 'High', 'Medium', 'Low', 'NotAssessed'];
  return {
    categories: d.comparison.map((s) => ({
      key: String(s.sprintId),
      label: s.current ? `${s.name} (this)` : s.name,
    })),
    series: severities
      .filter((sev) =>
        d.comparison.some((s) => s.bySeverity.some((b) => b.severity === sev && b.count)),
      )
      .map((sev) => ({
        key: sev,
        name: SEVERITY_META[sev].label,
        color: SEVERITY_META[sev].colorToken,
        values: d.comparison.map((s) => s.bySeverity.find((b) => b.severity === sev)?.count ?? 0),
      })),
  };
}

/** Issue-list params for a click on the sprint dashboard. */
export function sprintClickThrough(
  chart: 'progress' | 'heatmap' | 'comparison',
  selection: ChartSelection,
  filters: DashboardFilters,
  d: SprintDashboard,
): Params {
  const scope = scopeParams(filters);
  switch (chart) {
    case 'progress':
      return {
        ...scope,
        status: d.progress.find((g) => g.group === selection.series)?.statuses.join(','),
      };
    case 'heatmap': {
      const [moduleId, severity] = selection.key.split('|');
      return { ...scope, module: moduleId, severity };
    }
    case 'comparison':
      return { ...scope, sprint: selection.key, severity: selection.series };
  }
}

/**
 * Sprint dashboard (plan 9.2): header, progress, burn-down, issues found per testing day, module × severity, issues by
 * type, reopened issues and the comparison with the project's other sprints. Linked from the Testing page.
 */
@Component({
  selector: 'app-sprint-dashboard-page',
  imports: [
    MatButton,
    RouterLink,
    ChartBar,
    ChartHeatmap,
    ChartLine,
    ChartStackedBar,
    DashboardFilterBar,
    KpiCard,
    IstDatePipe,
    EmptyState,
    PageHeader,
    StatusPill,
    SprintReportButton,
    SprintStatusBadge,
  ],
  templateUrl: './sprint-dashboard-page.html',
  styles: `
    .qa-facts {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
      gap: 12px 16px;
    }
    .qa-facts dt {
      color: var(--qa-text-muted);
      font-size: var(--qa-text-sm);
    }
    .qa-facts dd {
      margin: 2px 0 0;
      font-weight: 500;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SprintDashboardPage {
  private readonly api = inject(AnalyticsApi);
  private readonly sprintsApi = inject(SprintsApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly filters = injectDashboardFilters();

  private readonly sprints = rxResource({
    params: () => this.filters().projectId ?? undefined,
    stream: ({ params }) => this.sprintsApi.list(params).pipe(catchError(() => of([]))),
  });

  protected readonly dashboard = rxResource({
    params: () => this.filters().sprintId ?? undefined,
    stream: ({ params }) => this.api.sprint(params),
  });
  protected readonly data = computed(() =>
    this.dashboard.hasValue() ? this.dashboard.value() : null,
  );
  protected readonly loading = computed(() => this.dashboard.isLoading() && !this.data());
  protected readonly error = computed(() =>
    this.dashboard.error() ? problemMessage(this.dashboard.error()) : null,
  );

  protected readonly progress = computed(() => {
    const d = this.data();
    return d ? progressSeries(d) : [];
  });
  protected readonly progressCategory = computed<ChartCategory[]>(() => [
    { key: 'sprint', label: 'Issues' },
  ]);
  protected readonly burndown = computed(() => {
    const d = this.data();
    return d ? burndown(d) : { categories: [], series: [] };
  });
  protected readonly found = computed<ChartItem[]>(() =>
    (this.data()?.foundPerDay ?? []).map((f) => ({
      key: f.date,
      label: shortDay(f.date),
      value: f.count,
    })),
  );
  protected readonly heatmap = computed<HeatmapData>(() => {
    const d = this.data();
    return d ? heatmap(d) : { rows: [], columns: [], values: [], color: '--qa-primary' };
  });
  protected readonly types = computed<ChartItem[]>(() =>
    (this.data()?.byType ?? []).map((t) => ({ key: t.type, label: t.type, value: t.count })),
  );
  protected readonly comparison = computed(() => {
    const d = this.data();
    return d ? comparison(d) : { categories: [], series: [] };
  });
  /** Height of the bottom row: the heat map grows with the number of modules. */
  protected readonly lowerHeight = computed(() =>
    Math.max(240, this.heatmap().rows.length * 44 + 80),
  );
  protected readonly burndownNote = computed(() => {
    const d = this.data();
    if (!d) {
      return '';
    }
    return d.sprint.testingCompletedAt
      ? 'Open issues at the end of each day since testing completed; the dashed line reaches zero on the latest due date.'
      : 'Starts when the tester marks testing completed.';
  });

  constructor() {
    // A project without a sprint in the URL shows its newest sprint.
    effect(() => {
      const f = this.filters();
      const sprints = this.sprints.value();
      if (f.projectId && !f.sprintId && sprints?.length) {
        const latest = latestSprint(sprints);
        untracked(() => {
          if (latest) {
            void this.router.navigate([], {
              relativeTo: this.route,
              queryParams: dashboardFiltersToParams({ ...f, sprintId: latest.id }),
              replaceUrl: true,
            });
          }
        });
      }
    });
    reloadOnIssueChanges(
      () => this.dashboard.reload(),
      (e) => e.sprintId === this.filters().sprintId,
      1500,
    );
  }

  protected open(chart: 'progress' | 'heatmap' | 'comparison', selection: ChartSelection): void {
    const d = this.data();
    if (d) {
      void this.router.navigate(['/issues'], {
        queryParams: sprintClickThrough(chart, selection, this.filters(), d),
      });
    }
  }
}
