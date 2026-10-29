import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { Params, Router, RouterLink } from '@angular/router';
import { problemMessage } from '../../core/api/api';
import { reloadOnIssueChanges } from '../../core/realtime/realtime.service';
import {
  ChartCategory,
  ChartItem,
  ChartSelection,
  ChartSeries,
} from '../../shared/charts/chart-options';
import { ChartBar, ChartStackedBar } from '../../shared/charts/charts';
import {
  DashboardFilterBar,
  DashboardFilters,
  analyticsQuery,
  injectDashboardFilters,
  scopeParams,
} from '../../shared/charts/dashboard-filters';
import { AnalyticsApi, ClientCard, Clients, Health } from '../../shared/data/analytics.api';
import { SPRINT_STATUS_LABELS } from '../../shared/data/sprints.api';
import { SEVERITIES, SEVERITY_META } from '../../shared/models/issue-enums';
import { IstDatePipe } from '../../shared/pipes/ist-date.pipe';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { HealthBadge } from '../../shared/ui/health-badge/health-badge';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { OPEN_STATUSES } from './overview-page';

const HEALTH_ORDER: Readonly<Record<Health, number>> = { Red: 0, Amber: 1, Green: 2 };

/** Client cards, the ones that need attention first (red, amber, green), then by name. */
export function sortedClients(d: Clients): ClientCard[] {
  return [...d.clients].sort(
    (a, b) => HEALTH_ORDER[a.health] - HEALTH_ORDER[b.health] || a.name.localeCompare(b.name),
  );
}

/** Open by client & severity (plan 9.5): one bar per client, stacked by severity (only severities that occur). */
export function clientSeverity(d: Clients): { categories: ChartCategory[]; series: ChartSeries[] } {
  const rows = d.openBySeverity;
  return {
    categories: rows.map((c) => ({ key: String(c.clientId), label: c.name })),
    series: SEVERITIES.filter((sev) =>
      rows.some((c) => c.bySeverity.some((b) => b.severity === sev && b.count)),
    ).map((sev) => ({
      key: sev,
      name: SEVERITY_META[sev].label,
      color: SEVERITY_META[sev].colorToken,
      values: rows.map((c) => c.bySeverity.find((b) => b.severity === sev)?.count ?? 0),
    })),
  };
}

/** UAT vs LIVE per project by the found-on environment type; "Other" and "Not set" only when they occur. */
export function uatVsLive(d: Clients): { categories: ChartCategory[]; series: ChartSeries[] } {
  const rows = d.uatVsLive;
  const parts = [
    { key: 'uat', name: 'UAT', color: '--qa-chart-1' },
    { key: 'live', name: 'LIVE', color: '--qa-chart-2' },
    { key: 'other', name: 'Other', color: '--qa-chart-4' },
    { key: 'notSet', name: 'Not set', color: '--qa-text-muted' },
  ] as const;
  return {
    categories: rows.map((p) => ({ key: String(p.projectId), label: p.projectKey })),
    series: parts
      .filter((part) => part.key === 'uat' || part.key === 'live' || rows.some((r) => r[part.key]))
      .map((part) => ({ ...part, values: rows.map((r) => r[part.key]) })),
  };
}

export function closureItems(d: Clients): ChartItem[] {
  return d.averageClosureDays.map((c) => ({
    key: String(c.clientId),
    label: `${c.name} (${c.count})`,
    value: Math.round(c.value * 10) / 10,
  }));
}

/** "2.5 d" – working days. */
export const days = (value: number): string => `${value} d`;

export function clientSeverityClickThrough(
  selection: ChartSelection,
  filters: DashboardFilters,
): Params {
  return {
    ...scopeParams(filters),
    client: selection.key,
    status: OPEN_STATUSES,
    severity: selection.series,
  };
}

/**
 * Client health dashboard (plan 9.5): a card per client with its health badge (red if a critical issue is open or
 * more than 3 are overdue, amber if anything is overdue), open by severity, UAT vs LIVE and closure time.
 */
@Component({
  selector: 'app-clients-page',
  imports: [
    MatButton,
    RouterLink,
    ChartBar,
    ChartStackedBar,
    DashboardFilterBar,
    EmptyState,
    HealthBadge,
    IstDatePipe,
    PageHeader,
  ],
  templateUrl: './clients-page.html',
  styles: `
    .qa-client-card[data-health='Red'] {
      border-left: 3px solid var(--qa-severity-critical);
    }
    .qa-client-card[data-health='Amber'] {
      border-left: 3px solid var(--qa-severity-medium);
    }
    .qa-client-stat {
      display: block;
      padding: 6px 8px;
      border-radius: var(--qa-radius-control);
      color: inherit;
      text-decoration: none;
    }
    .qa-client-stat:hover {
      background: var(--qa-surface-muted);
    }
    .qa-client-stat:focus-visible {
      outline: 2px solid var(--qa-focus-ring);
      outline-offset: 1px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClientsPage {
  private readonly api = inject(AnalyticsApi);
  private readonly router = inject(Router);

  protected readonly filters = injectDashboardFilters();
  protected readonly scope = computed(() => scopeParams(this.filters()));
  protected readonly openStatuses = OPEN_STATUSES;
  protected readonly sprintStatus = SPRINT_STATUS_LABELS;
  protected readonly days = days;
  protected readonly skeletons = [1, 2, 3];

  protected readonly clients = rxResource({
    params: () => analyticsQuery(this.filters()),
    stream: ({ params }) => this.api.clients(params),
  });
  protected readonly data = computed(() => (this.clients.hasValue() ? this.clients.value() : null));
  protected readonly loading = computed(() => this.clients.isLoading() && !this.data());
  protected readonly error = computed(() =>
    this.clients.error() ? problemMessage(this.clients.error()) : null,
  );

  protected readonly cards = computed(() => {
    const d = this.data();
    return d ? sortedClients(d) : [];
  });
  protected readonly severity = computed(() => {
    const d = this.data();
    return d ? clientSeverity(d) : { categories: [], series: [] };
  });
  protected readonly environments = computed(() => {
    const d = this.data();
    return d ? uatVsLive(d) : { categories: [], series: [] };
  });
  protected readonly closure = computed(() => {
    const d = this.data();
    return d ? closureItems(d) : [];
  });
  protected readonly severityHeight = computed(() =>
    Math.max(200, this.severity().categories.length * 40 + 80),
  );

  constructor() {
    reloadOnIssueChanges(() => this.clients.reload(), undefined, 1500);
  }

  /** Card numbers open the client's issues; the card scope replaces the client filter. */
  protected cardParams(card: ClientCard, extra: Params): Params {
    return { ...this.scope(), client: card.clientId, ...extra };
  }

  protected openSeverity(selection: ChartSelection): void {
    void this.router.navigate(['/issues'], {
      queryParams: clientSeverityClickThrough(selection, this.filters()),
    });
  }
}
