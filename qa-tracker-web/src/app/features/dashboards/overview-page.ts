import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { Params, Router, RouterLink } from '@angular/router';
import { problemMessage } from '../../core/api/api';
import { reloadOnIssueChanges } from '../../core/realtime/realtime.service';
import { ChartItem, ChartSelection } from '../../shared/charts/chart-options';
import { ChartBar, ChartDonut, ChartLine } from '../../shared/charts/charts';
import {
  DashboardFilterBar,
  DashboardFilters,
  analyticsQuery,
  injectDashboardFilters,
  scopeParams,
} from '../../shared/charts/dashboard-filters';
import { KpiCard, trendText } from '../../shared/charts/kpi-card';
import { AnalyticsApi, Overview, StatusGroup } from '../../shared/data/analytics.api';
import { SEVERITY_META } from '../../shared/models/issue-enums';
import { IstDatePipe, relativeTime } from '../../shared/pipes/ist-date.pipe';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { UserAvatar } from '../../shared/ui/user-avatar/user-avatar';

/** Open = still waiting for a fix (ADR-031); the click-through statuses of the "open" numbers. */
export const OPEN_STATUSES = 'New,Assigned,InProgress,NeedInfo,OnHold,Disputed,Reopened';

const GROUP_META: Readonly<Record<StatusGroup, { label: string; color: string }>> = {
  Open: { label: 'Open', color: '--qa-status-assigned' },
  Fixed: { label: 'Fixed', color: '--qa-status-fixed' },
  Verified: { label: 'Verified', color: '--qa-status-verified' },
  Closed: { label: 'Closed', color: '--qa-status-closed' },
  Rejected: { label: 'Rejected', color: '--qa-status-rejected' },
};

/** "27 Oct" axis labels for days (the full date is in the table view). */
export function shortDay(iso: string): string {
  const [, m, d] = iso.split('-').map(Number);
  const months = [
    'Jan',
    'Feb',
    'Mar',
    'Apr',
    'May',
    'Jun',
    'Jul',
    'Aug',
    'Sep',
    'Oct',
    'Nov',
    'Dec',
  ];
  return `${String(d).padStart(2, '0')}/${months[m - 1]}`;
}

export function statusGroupItems(overview: Overview): ChartItem[] {
  return overview.statusGroups.map((g) => ({
    key: g.group,
    label: GROUP_META[g.group].label,
    value: g.count,
    color: GROUP_META[g.group].color,
  }));
}

export function severityItems(overview: Overview): ChartItem[] {
  return overview.openBySeverity.map((s) => ({
    key: s.severity,
    label: SEVERITY_META[s.severity].label,
    value: s.count,
    color: SEVERITY_META[s.severity].colorToken,
  }));
}

/** Issue-list query params for a click on the overview (plan 9: every chart opens the filtered list). */
export function clickThrough(
  chart: 'status' | 'severity' | 'project',
  selection: ChartSelection,
  filters: DashboardFilters,
  overview: Overview,
): Params {
  const scope = scopeParams(filters);
  switch (chart) {
    case 'status': {
      const group = overview.statusGroups.find((g) => g.group === selection.key);
      return { ...scope, status: group?.statuses.join(',') };
    }
    case 'severity':
      return { ...scope, status: OPEN_STATUSES, severity: selection.key };
    case 'project':
      return { ...scope, project: selection.key, sprint: null, status: OPEN_STATUSES };
  }
}

/**
 * Overview dashboard – the home page (plan 9.1): KPIs, issues by status, open by severity, created vs closed,
 * open by project and recent activity. Every number opens the issue list behind it.
 */
@Component({
  selector: 'app-overview-page',
  imports: [
    MatButton,
    RouterLink,
    ChartBar,
    ChartDonut,
    ChartLine,
    DashboardFilterBar,
    KpiCard,
    IstDatePipe,
    PageHeader,
    UserAvatar,
  ],
  templateUrl: './overview-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OverviewPage {
  private readonly api = inject(AnalyticsApi);
  private readonly router = inject(Router);

  protected readonly filters = injectDashboardFilters();
  protected readonly scope = computed(() => scopeParams(this.filters()));
  protected readonly openStatuses = OPEN_STATUSES;

  protected readonly overview = rxResource({
    params: () => analyticsQuery(this.filters()),
    stream: ({ params }) => this.api.overview(params),
  });
  protected readonly data = computed(() =>
    this.overview.hasValue() ? this.overview.value() : null,
  );
  protected readonly loading = computed(() => this.overview.isLoading() && !this.data());
  protected readonly error = computed(() =>
    this.overview.error() ? problemMessage(this.overview.error()) : null,
  );

  protected readonly openTrend = computed(() => {
    const k = this.data()?.kpis;
    return k ? trendText(k.open, k.openWeekAgo, 'since last week') : '';
  });
  protected readonly statusItems = computed(() => {
    const d = this.data();
    return d ? statusGroupItems(d) : [];
  });
  protected readonly severityItems = computed(() => {
    const d = this.data();
    return d ? severityItems(d) : [];
  });
  protected readonly projectItems = computed<ChartItem[]>(() =>
    (this.data()?.openByProject ?? []).map((p) => ({
      key: String(p.projectId),
      label: p.projectKey,
      value: p.count,
    })),
  );
  protected readonly days = computed(() =>
    (this.data()?.createdVsClosed ?? []).map((d) => ({ key: d.date, label: shortDay(d.date) })),
  );
  protected readonly flow = computed(() => {
    const flow = this.data()?.createdVsClosed ?? [];
    return [
      {
        key: 'created',
        name: 'Created',
        color: '--qa-primary',
        values: flow.map((d) => d.created),
      },
      {
        key: 'closed',
        name: 'Closed',
        color: '--qa-status-closed',
        values: flow.map((d) => d.closed),
      },
    ];
  });

  constructor() {
    reloadOnIssueChanges(() => this.overview.reload(), undefined, 1500);
  }

  protected open(chart: 'status' | 'severity' | 'project', selection: ChartSelection): void {
    const overview = this.data();
    if (overview) {
      void this.router.navigate(['/issues'], {
        queryParams: clickThrough(chart, selection, this.filters(), overview),
      });
    }
  }

  protected when(iso: string): string {
    return relativeTime(iso);
  }
}
