import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { convertToParamMap, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideFakeAuth, testUser } from '../../../testing/fake-auth';
import { dashboardFiltersFromParams } from '../../shared/charts/dashboard-filters';
import { provideCharts } from '../../shared/charts/echarts';
import { Overview } from '../../shared/data/analytics.api';
import {
  OPEN_STATUSES,
  OverviewPage,
  clickThrough,
  severityItems,
  shortDay,
  statusGroupItems,
} from './overview-page';

const overview: Overview = {
  from: '2026-10-28',
  to: '2026-10-29',
  kpis: {
    open: 6,
    openWeekAgo: 4,
    awaitingRetest: 3,
    overdue: 1,
    criticalOpen: 0,
    closedThisWeek: 1,
  },
  statusGroups: [
    { group: 'Open', statuses: ['New', 'Assigned', 'Reopened'], count: 6 },
    { group: 'Fixed', statuses: ['Fixed'], count: 2 },
    { group: 'Verified', statuses: ['Verified'], count: 0 },
    { group: 'Closed', statuses: ['Closed'], count: 1 },
    { group: 'Rejected', statuses: ['Rejected'], count: 0 },
  ],
  openBySeverity: [
    { severity: 'Critical', count: 0 },
    { severity: 'High', count: 4 },
    { severity: 'Medium', count: 0 },
    { severity: 'Low', count: 2 },
    { severity: 'NotAssessed', count: 0 },
  ],
  createdVsClosed: [
    { date: '2026-10-28', created: 9, closed: 1 },
    { date: '2026-10-29', created: 0, closed: 0 },
  ],
  openByProject: [{ projectId: 2, projectKey: 'UICHK', projectName: 'UI Check', count: 3 }],
  recentActivity: [
    {
      id: 10,
      kind: 'StatusChanged',
      issueKey: 'UICHK-4',
      issueTitle: 'Customer search misses names with spaces',
      userName: 'Emily Developer',
      userAvatarColor: null,
      summary: 'Emily Developer started work',
      createdAt: '2026-10-29T08:00:00Z',
    },
  ],
};

describe('overview helpers', () => {
  const filters = dashboardFiltersFromParams(convertToParamMap({ client: '2', project: '7' }));

  it('maps status groups and severities to chart items with token colours', () => {
    expect(statusGroupItems(overview)[0]).toEqual({
      key: 'Open',
      label: 'Open',
      value: 6,
      color: '--qa-status-assigned',
    });
    expect(severityItems(overview).map((i) => i.color)).toContain('--qa-severity-high');
    expect(shortDay('2026-09-30')).toBe('30/Sep');
  });

  it('opens the issue list with the scope and what was clicked', () => {
    expect(clickThrough('status', { key: 'Open' }, filters, overview)).toEqual({
      client: 2,
      project: 7,
      sprint: null,
      status: 'New,Assigned,Reopened',
    });
    expect(clickThrough('severity', { key: 'High' }, filters, overview)).toMatchObject({
      status: OPEN_STATUSES,
      severity: 'High',
    });
    expect(clickThrough('project', { key: '9' }, filters, overview)).toMatchObject({
      project: '9',
    });
  });
});

describe('OverviewPage', () => {
  beforeAll(() => {
    // jsdom has no ResizeObserver (ngx-echarts autoResize); charts only need it to exist here.
    globalThis.ResizeObserver ??= class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    };
  });

  it('loads the overview for the URL filters and shows the KPIs and activity', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'home', component: OverviewPage, providers: [provideCharts()] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeAuth(testUser('Tester')),
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create('/home?client=2&period=7');

    const request = http.expectOne((r) => r.url.endsWith('/analytics/overview'));
    expect(request.request.params.get('clientId')).toBe('2');
    expect(request.request.params.get('from')).toBeTruthy();
    request.flush(overview);
    http.match(() => true).forEach((r) => r.flush([]));
    await harness.fixture.whenStable();

    const el = harness.routeNativeElement!;
    const kpis = [...el.querySelectorAll('app-kpi-card')].map((k) =>
      k.textContent!.replace(/\s+/g, ' ').trim(),
    );
    expect(kpis[0]).toBe('Open issues6+2 since last week'); // label, value, note (block spans)
    expect(kpis[2]).toContain('Overdue1');
    expect(el.querySelector('app-kpi-card.qa-attention')!.textContent).toContain('Overdue');
    expect(el.textContent).toContain('Emily Developer started work');
    expect(el.querySelectorAll('app-chart').length).toBe(4);
  });
});
