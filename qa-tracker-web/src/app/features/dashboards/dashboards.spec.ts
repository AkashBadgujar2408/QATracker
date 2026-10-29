import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, convertToParamMap, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideFakeAuth, testUser } from '../../../testing/fake-auth';
import { dashboardFiltersFromParams } from '../../shared/charts/dashboard-filters';
import { provideCharts } from '../../shared/charts/echarts';
import { Developers, SprintDashboard } from '../../shared/data/analytics.api';
import { Sprint } from '../../shared/data/sprints.api';
import {
  DevelopersPage,
  MAX_LINES,
  fixedPerWeek,
  hours,
  metricItems,
  workload,
  workloadClickThrough,
} from './developers-page';
import {
  SprintDashboardPage,
  burndown,
  comparison,
  heatmap,
  latestSprint,
  progressSeries,
  sprintClickThrough,
} from './sprint-dashboard-page';

const sprintDashboard: SprintDashboard = {
  sprint: {
    id: 3,
    name: 'Sprint 2 – UAT',
    projectId: 7,
    projectKey: 'PGERP',
    projectName: 'ERP',
    clientName: 'Acme Gears',
    phaseName: 'UAT',
    status: 'TestingCompleted',
    startedAt: '2026-10-20T04:00:00Z',
    testingDays: 6,
    testingCompletedAt: '2026-10-27T12:00:00Z',
    closedAt: null,
    issueCount: 4,
  },
  progress: [
    { group: 'Open', statuses: ['New', 'Reopened'], count: 2 },
    { group: 'Fixed', statuses: ['Fixed'], count: 1 },
    { group: 'Verified', statuses: ['Verified'], count: 0 },
    { group: 'Closed', statuses: ['Closed'], count: 1 },
    { group: 'Rejected', statuses: ['Rejected'], count: 0 },
  ],
  burndown: {
    latestDueDate: '2026-10-29',
    points: [
      { date: '2026-10-27', open: 4, ideal: 4 },
      { date: '2026-10-28', open: 3, ideal: 2 },
      { date: '2026-10-29', open: null, ideal: 0 },
    ],
  },
  foundPerDay: [{ date: '2026-10-20', count: 4 }],
  moduleSeverity: {
    modules: [
      { id: 11, name: 'Sales' },
      { id: 12, name: 'Billing' },
    ],
    severities: ['Critical', 'High', 'Medium', 'Low', 'NotAssessed'],
    cells: [
      { moduleId: 11, severity: 'High', count: 3 },
      { moduleId: 12, severity: 'Low', count: 1 },
    ],
  },
  byType: [{ type: 'Bug', count: 4 }],
  reopened: {
    count: 1,
    items: [
      {
        key: 'PGERP-2',
        title: 'Totals wrong',
        reopenCount: 2,
        status: 'Reopened',
        assigneeName: 'Emily Developer',
      },
    ],
  },
  comparison: [
    {
      sprintId: 2,
      name: 'Sprint 1 – Early',
      phaseName: 'Early',
      current: false,
      total: 2,
      bySeverity: [{ severity: 'High', count: 2 }],
    },
    {
      sprintId: 3,
      name: 'Sprint 2 – UAT',
      phaseName: 'UAT',
      current: true,
      total: 4,
      bySeverity: [
        { severity: 'High', count: 3 },
        { severity: 'Low', count: 1 },
      ],
    },
  ],
};

const developers: Developers = {
  from: '2026-09-30',
  to: '2026-10-29',
  workload: [
    {
      userId: 5,
      name: 'Emily Developer',
      avatarColor: null,
      assigned: 2,
      inProgress: 1,
      needInfo: 0,
      onHold: 1,
    },
  ],
  fixedPerWeek: {
    weeks: ['2026-10-19', '2026-10-26'],
    series: Array.from({ length: MAX_LINES + 2 }, (_, i) => ({
      key: i + 1,
      name: `Dev ${i + 1}`,
      values: [1, i],
    })),
  },
  averageFixHours: [{ userId: 5, name: 'Emily Developer', avatarColor: null, value: 0, count: 2 }],
  firstTimeRight: [{ userId: 5, name: 'Emily Developer', avatarColor: null, value: 0.5, count: 2 }],
  sla: { onTime: 2, total: 2, rate: 1 },
  me: { open: 3, overdue: 1, fixedThisWeek: 2, averageFixHours: 12.5 },
};

describe('sprint dashboard helpers', () => {
  const filters = dashboardFiltersFromParams(
    convertToParamMap({ client: '1', project: '7', sprint: '3' }),
  );

  it('picks the newest sprint of a project', () => {
    const sprint = (id: number, startedAt: string) => ({ id, startedAt }) as Sprint;
    expect(
      latestSprint([sprint(1, '2026-09-01T00:00:00Z'), sprint(2, '2026-10-01T00:00:00Z')])?.id,
    ).toBe(2);
    expect(latestSprint([])).toBeNull();
  });

  it('builds progress, burn-down, heat map and comparison data', () => {
    expect(progressSeries(sprintDashboard).map((s) => s.values[0])).toEqual([2, 1, 0, 1, 0]);
    const b = burndown(sprintDashboard);
    expect(b.categories.map((c) => c.label)).toEqual(['27/Oct', '28/Oct', '29/Oct']);
    expect(b.series[0].values).toEqual([4, 3, null]);
    expect(b.series[1].dashed).toBe(true);

    const h = heatmap(sprintDashboard);
    expect(h.columns.map((c) => c.key)).toEqual(['High', 'Low']); // only severities that occur
    expect(h.values).toEqual([
      [3, 0],
      [0, 1],
    ]);

    const c = comparison(sprintDashboard);
    expect(c.categories.map((x) => x.label)).toEqual(['Sprint 1 – Early', 'Sprint 2 – UAT (this)']);
    expect(c.series.map((s) => s.key)).toEqual(['High', 'Low']);
    expect(c.series[0].values).toEqual([2, 3]);
  });

  it('opens the issue list for the clicked part', () => {
    expect(
      sprintClickThrough('progress', { key: 'sprint', series: 'Open' }, filters, sprintDashboard),
    ).toEqual({
      client: 1,
      project: 7,
      sprint: 3,
      status: 'New,Reopened',
    });
    expect(
      sprintClickThrough('heatmap', { key: '11|High' }, filters, sprintDashboard),
    ).toMatchObject({
      sprint: 3,
      module: '11',
      severity: 'High',
    });
    expect(
      sprintClickThrough('comparison', { key: '2', series: 'High' }, filters, sprintDashboard),
    ).toMatchObject({
      sprint: '2',
      severity: 'High',
    });
  });
});

describe('developer dashboard helpers', () => {
  it('stacks the workload parts and opens their statuses', () => {
    const w = workload(developers);
    expect(w.categories).toEqual([{ key: '5', label: 'Emily Developer' }]);
    expect(w.series.map((s) => s.values[0])).toEqual([2, 1, 0, 1]);
    const filters = dashboardFiltersFromParams(convertToParamMap({ project: '7' }));
    expect(workloadClickThrough({ key: '5', series: 'assigned' }, filters)).toMatchObject({
      project: 7,
      assignee: '5',
      status: 'Assigned,Reopened',
    });
  });

  it('gives the busiest developers a colour each and sums the rest as Others', () => {
    const f = fixedPerWeek(developers);
    expect(f.series).toHaveLength(MAX_LINES + 1);
    expect(f.series.slice(0, MAX_LINES).map((s) => s.color)).toEqual(
      Array.from({ length: MAX_LINES }, (_, i) => `--qa-chart-${i + 1}`),
    );
    expect(f.series[MAX_LINES]).toMatchObject({ name: 'Others (2)', values: [2, 11] });
    expect(f.categories[0].label).toBe('Week of 19/Oct');
  });

  it('labels metrics with the issue count and scales rates to %', () => {
    expect(metricItems(developers.firstTimeRight, 100)).toEqual([
      { key: '5', label: 'Emily Developer (2)', value: 50 },
    ]);
    expect(hours(12.5)).toBe('12.5 h');
  });
});

describe('dashboard pages', () => {
  beforeAll(() => {
    // jsdom has no ResizeObserver (ngx-echarts autoResize).
    globalThis.ResizeObserver ??= class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    };
  });

  function setup(role: 'Developer' | 'Tester') {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'sprint', component: SprintDashboardPage, providers: [provideCharts()] },
          { path: 'developers', component: DevelopersPage, providers: [provideCharts()] },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeAuth(testUser(role, { id: 5 })),
      ],
    });
    return TestBed.inject(HttpTestingController);
  }

  it('opens the newest sprint of the project and shows its header', async () => {
    const http = setup('Tester');
    const harness = await RouterTestingHarness.create('/sprint?client=1&project=7');
    http.match((r) => r.url.includes('/lookups/cascade')).forEach((r) => r.flush(null));
    http
      .match((r) => r.url.endsWith('/projects/7/sprints'))
      .forEach((r) =>
        r.flush([
          { id: 2, startedAt: '2026-09-01T00:00:00Z' },
          { id: 3, startedAt: '2026-10-20T00:00:00Z' },
        ]),
      );
    TestBed.tick();
    await new Promise((resolve) => setTimeout(resolve)); // the navigation to the newest sprint
    TestBed.tick();
    expect(TestBed.inject(Router).url).toContain('sprint=3');

    http.expectOne((r) => r.url.endsWith('/analytics/sprints/3')).flush(sprintDashboard);
    http.match(() => true).forEach((r) => r.flush([]));
    await harness.fixture.whenStable();
    const el = harness.routeNativeElement!;
    expect(el.querySelector('#sprint-name')!.textContent).toContain('Sprint 2 – UAT');
    expect(el.textContent).toContain('Testing days');
    expect(el.textContent).toContain('PGERP-2');
  });

  it('shows My stats to developers with the formatted fix time', async () => {
    const http = setup('Developer');
    const harness = await RouterTestingHarness.create('/developers');
    http.expectOne((r) => r.url.endsWith('/analytics/developers')).flush(developers);
    http.match(() => true).forEach((r) => r.flush([]));
    await harness.fixture.whenStable();
    const el = harness.routeNativeElement!;
    const cards = [...el.querySelectorAll('app-kpi-card')].map((k) =>
      k.textContent!.replace(/\s+/g, ''),
    );
    expect(cards[0]).toContain('Myopenissues3');
    expect(cards[3]).toContain('12.5h');
    expect(el.querySelector('app-kpi-card.qa-attention')!.textContent).toContain('My overdue');
  });

  it('hides My stats from testers', async () => {
    const http = setup('Tester');
    const harness = await RouterTestingHarness.create('/developers');
    http.expectOne((r) => r.url.endsWith('/analytics/developers')).flush(developers);
    http.match(() => true).forEach((r) => r.flush([]));
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.textContent).not.toContain('My stats');
  });
});
