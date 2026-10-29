import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { Params, Router } from '@angular/router';
import { problemMessage } from '../../core/api/api';
import { reloadOnIssueChanges } from '../../core/realtime/realtime.service';
import {
  ChartCategory,
  ChartItem,
  ChartSelection,
  ChartSeries,
} from '../../shared/charts/chart-options';
import {
  ChartBar,
  ChartDonut,
  ChartLine,
  ChartStackedBar,
  ChartTreemap,
} from '../../shared/charts/charts';
import {
  DashboardFilterBar,
  DashboardFilters,
  analyticsQuery,
  injectDashboardFilters,
  scopeParams,
} from '../../shared/charts/dashboard-filters';
import { KpiCard } from '../../shared/charts/kpi-card';
import { AnalyticsApi, Quality } from '../../shared/data/analytics.api';
import { REJECT_TYPE_LABELS } from '../../shared/data/issues.api';
import { SEVERITIES, SEVERITY_META } from '../../shared/models/issue-enums';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { hours, percent } from './developers-page';
import { shortDay } from './overview-page';

/** Categorical colours for identities such as phases (ADR-033); more than six are summed as "Others". */
const CATEGORICAL = 6;

/** Defect leakage (plan 9.4): issues per project stacked by sprint phase, phases in their sort order. */
export function leakage(d: Quality): { categories: ChartCategory[]; series: ChartSeries[] } {
  const phases: { id: number; name: string }[] = [];
  for (const project of d.leakage) {
    for (const p of project.byPhase) {
      if (!phases.some((x) => x.id === p.phaseId)) {
        phases.push({ id: p.phaseId, name: p.phase });
      }
    }
  }
  const count = (projectIndex: number, phaseId: number): number =>
    d.leakage[projectIndex].byPhase.find((p) => p.phaseId === phaseId)?.count ?? 0;
  const top = phases.slice(0, CATEGORICAL);
  const rest = phases.slice(CATEGORICAL);
  const series: ChartSeries[] = top.map((phase, i) => ({
    key: String(phase.id),
    name: phase.name,
    color: `--qa-chart-${i + 1}`,
    values: d.leakage.map((_, p) => count(p, phase.id)),
  }));
  if (rest.length) {
    series.push({
      key: 'others',
      name: `Others (${rest.length})`,
      color: '--qa-text-muted',
      values: d.leakage.map((_, p) => rest.reduce((sum, phase) => sum + count(p, phase.id), 0)),
    });
  }
  return {
    categories: d.leakage.map((p) => ({ key: String(p.projectId), label: p.projectKey })),
    series,
  };
}

/** Severity trend (plan 9.4): new issues per week, stacked by severity (only severities that occur). */
export function severityTrend(d: Quality): { categories: ChartCategory[]; series: ChartSeries[] } {
  const trend = d.severityTrend;
  return {
    categories: trend.weeks.map((w) => ({ key: w, label: `Week of ${shortDay(w)}` })),
    series: SEVERITIES.flatMap((severity) => {
      const line = trend.series.find((s) => s.key === severity);
      return line && line.values.some((v) => v)
        ? [
            {
              key: severity,
              name: SEVERITY_META[severity].label,
              color: SEVERITY_META[severity].colorToken,
              values: line.values,
            },
          ]
        : [];
    }),
  };
}

export function turnaroundItems(d: Quality): ChartItem[] {
  return d.retestTurnaround.byEnvironmentType.map((t) => ({
    key: t.environmentType,
    label: `${t.environmentType} (${t.count})`,
    value: Math.round(t.medianHours * 10) / 10,
  }));
}

export function ageingItems(d: Quality): ChartItem[] {
  return d.retestAgeing.map((a) => ({
    key: a.bucket,
    label: a.label,
    value: a.count,
    color: a.bucket === 'OverFiveDays' ? '--qa-severity-critical' : '--qa-primary',
  }));
}

export function hotspotItems(d: Quality): ChartItem[] {
  return d.moduleHotspots.map((m) => ({
    key: String(m.moduleId),
    label: `${m.module} (${m.projectKey})`,
    value: m.count,
  }));
}

export function rejectedItems(d: Quality): ChartItem[] {
  return d.rejected.byType.map((r, i) => ({
    key: r.type,
    label: REJECT_TYPE_LABELS[r.type],
    value: r.count,
    color: `--qa-chart-${i + 1}`,
  }));
}

/** Where a click on the quality dashboard leads: the issue list behind the number (or the retest queue). */
export function qualityClickThrough(
  chart: 'hotspot' | 'rejected' | 'ageing',
  selection: ChartSelection,
  filters: DashboardFilters,
): { link: string; params: Params } {
  const scope = scopeParams(filters);
  switch (chart) {
    case 'hotspot':
      return { link: '/issues', params: { ...scope, module: selection.key } };
    case 'rejected':
      return { link: '/issues', params: { ...scope, status: 'Rejected' } };
    case 'ageing':
      return { link: '/retest-queue', params: { project: filters.projectId } };
  }
}

/**
 * Quality and testing dashboard (plan 9.4): defect leakage by phase, retest turnaround and queue ageing, module
 * hotspots, rejected ratio and the weekly severity trend.
 */
@Component({
  selector: 'app-quality-page',
  imports: [
    MatButton,
    ChartBar,
    ChartDonut,
    ChartLine,
    ChartStackedBar,
    ChartTreemap,
    DashboardFilterBar,
    KpiCard,
    EmptyState,
    PageHeader,
  ],
  templateUrl: './quality-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QualityPage {
  private readonly api = inject(AnalyticsApi);
  private readonly router = inject(Router);

  protected readonly filters = injectDashboardFilters();
  protected readonly scope = computed(() => scopeParams(this.filters()));
  protected readonly hours = hours;

  protected readonly quality = rxResource({
    params: () => analyticsQuery(this.filters()),
    stream: ({ params }) => this.api.quality(params),
  });
  protected readonly data = computed(() => (this.quality.hasValue() ? this.quality.value() : null));
  protected readonly loading = computed(() => this.quality.isLoading() && !this.data());
  protected readonly error = computed(() =>
    this.quality.error() ? problemMessage(this.quality.error()) : null,
  );

  private readonly none = { categories: [], series: [] };
  protected readonly leakage = computed(() => {
    const d = this.data();
    return d ? leakage(d) : this.none;
  });
  protected readonly trend = computed(() => {
    const d = this.data();
    return d ? severityTrend(d) : this.none;
  });
  protected readonly turnaround = computed(() => {
    const d = this.data();
    return d ? turnaroundItems(d) : [];
  });
  protected readonly ageing = computed(() => {
    const d = this.data();
    return d ? ageingItems(d) : [];
  });
  protected readonly hotspots = computed(() => {
    const d = this.data();
    return d ? hotspotItems(d) : [];
  });
  protected readonly rejected = computed(() => {
    const d = this.data();
    return d ? rejectedItems(d) : [];
  });

  protected readonly medianTurnaround = computed(() => {
    const t = this.data()?.retestTurnaround;
    return !t ? null : t.count === 0 ? '–' : hours(Math.round(t.medianHours * 10) / 10);
  });
  protected readonly inQueue = computed(() => {
    const d = this.data();
    return d ? d.retestAgeing.reduce((sum, a) => sum + a.count, 0) : null;
  });
  protected readonly waitingLong = computed(
    () => this.data()?.retestAgeing.find((a) => a.bucket === 'OverFiveDays')?.count ?? null,
  );
  protected readonly rejectedRatio = computed(() => {
    const r = this.data()?.rejected;
    return !r ? null : r.total === 0 ? '–' : percent(Math.round(r.ratio * 1000) / 10);
  });

  constructor() {
    reloadOnIssueChanges(() => this.quality.reload(), undefined, 1500);
  }

  protected open(chart: 'hotspot' | 'rejected' | 'ageing', selection: ChartSelection): void {
    const target = qualityClickThrough(chart, selection, this.filters());
    void this.router.navigate([target.link], { queryParams: target.params });
  }
}
