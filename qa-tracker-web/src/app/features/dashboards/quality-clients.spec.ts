import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { convertToParamMap, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideFakeAuth, testUser } from '../../../testing/fake-auth';
import { dashboardFiltersFromParams } from '../../shared/charts/dashboard-filters';
import { provideCharts } from '../../shared/charts/echarts';
import { Clients, Quality } from '../../shared/data/analytics.api';
import {
  ClientsPage,
  clientSeverity,
  clientSeverityClickThrough,
  closureItems,
  sortedClients,
  uatVsLive,
} from './clients-page';
import {
  QualityPage,
  ageingItems,
  hotspotItems,
  leakage,
  qualityClickThrough,
  rejectedItems,
  severityTrend,
  turnaroundItems,
} from './quality-page';

const quality: Quality = {
  from: '2026-09-30',
  to: '2026-10-29',
  leakage: [
    {
      projectId: 7,
      projectKey: 'PGERP',
      projectName: 'ERP',
      byPhase: [
        { phaseId: 1, phase: 'Early', count: 45 },
        { phaseId: 2, phase: 'UAT', count: 12 },
      ],
    },
    {
      projectId: 8,
      projectKey: 'NOVA',
      projectName: 'Nova',
      byPhase: [{ phaseId: 3, phase: 'LIVE', count: 4 }],
    },
  ],
  retestTurnaround: {
    medianHours: 6.25,
    count: 9,
    byEnvironmentType: [
      { environmentType: 'UAT', medianHours: 5, count: 7 },
      { environmentType: 'LIVE', medianHours: 10.04, count: 2 },
    ],
  },
  retestAgeing: [
    { bucket: 'UnderOneDay', label: '< 1 day', count: 2 },
    { bucket: 'OneToTwoDays', label: '1–2 days', count: 1 },
    { bucket: 'ThreeToFiveDays', label: '3–5 days', count: 0 },
    { bucket: 'OverFiveDays', label: '> 5 days', count: 3 },
  ],
  moduleHotspots: [{ moduleId: 11, module: 'Billing', projectKey: 'PGERP', count: 14, open: 5 }],
  rejected: {
    rejected: 3,
    total: 40,
    ratio: 0.075,
    byType: [
      { type: 'Duplicate', count: 2 },
      { type: 'WontFix', count: 1 },
    ],
  },
  severityTrend: {
    weeks: ['2026-10-19', '2026-10-26'],
    series: [
      { key: 'High', name: 'High', values: [3, 1] },
      { key: 'Critical', name: 'Critical', values: [1, 0] },
      { key: 'Low', name: 'Low', values: [0, 0] },
    ],
  },
};

const clients: Clients = {
  from: '2026-09-30',
  to: '2026-10-29',
  clients: [
    {
      clientId: 1,
      name: 'Acme Gears',
      open: 4,
      critical: 0,
      overdue: 0,
      lastSprint: null,
      health: 'Green',
    },
    {
      clientId: 2,
      name: 'Blue Harbour',
      open: 9,
      critical: 1,
      overdue: 2,
      lastSprint: {
        sprintId: 30,
        name: 'Sprint 3 – LIVE',
        projectKey: 'NOVA',
        status: 'TestingCompleted',
        startedAt: '2026-10-27T04:00:00Z',
      },
      health: 'Red',
    },
    {
      clientId: 3,
      name: 'Cedar Labs',
      open: 2,
      critical: 0,
      overdue: 1,
      lastSprint: null,
      health: 'Amber',
    },
  ],
  openBySeverity: [
    {
      clientId: 2,
      name: 'Blue Harbour',
      bySeverity: [
        { severity: 'Critical', count: 1 },
        { severity: 'Medium', count: 8 },
      ],
    },
  ],
  uatVsLive: [
    { projectId: 8, projectKey: 'NOVA', projectName: 'Nova', uat: 5, live: 3, other: 0, notSet: 1 },
  ],
  averageClosureDays: [{ clientId: 2, name: 'Blue Harbour', value: 2.46, count: 6 }],
};

describe('quality dashboard helpers', () => {
  it('stacks leakage by phase with a categorical colour per phase', () => {
    const l = leakage(quality);
    expect(l.categories.map((c) => c.label)).toEqual(['PGERP', 'NOVA']);
    expect(l.series.map((s) => [s.name, s.color, s.values])).toEqual([
      ['Early', '--qa-chart-1', [45, 0]],
      ['UAT', '--qa-chart-2', [12, 0]],
      ['LIVE', '--qa-chart-3', [0, 4]],
    ]);
  });

  it('keeps only occurring severities in severity order for the trend', () => {
    const t = severityTrend(quality);
    expect(t.series.map((s) => s.key)).toEqual(['Critical', 'High']);
    expect(t.series[0].color).toBe('--qa-severity-critical');
    expect(t.categories[0].label).toBe('Week of 19/Oct');
  });

  it('labels turnaround, ageing, hotspots and reject reasons', () => {
    expect(turnaroundItems(quality).map((i) => [i.label, i.value])).toEqual([
      ['UAT (7)', 5],
      ['LIVE (2)', 10],
    ]);
    expect(ageingItems(quality)[3].color).toBe('--qa-severity-critical');
    expect(hotspotItems(quality)[0].label).toBe('Billing (PGERP)');
    expect(rejectedItems(quality).map((i) => i.label)).toEqual(['Duplicate', "Won't fix"]);
  });

  it('opens the module, the rejected issues or the retest queue', () => {
    const filters = dashboardFiltersFromParams(convertToParamMap({ client: '1', project: '7' }));
    expect(qualityClickThrough('hotspot', { key: '11' }, filters)).toEqual({
      link: '/issues',
      params: { client: 1, project: 7, sprint: null, module: '11' },
    });
    expect(qualityClickThrough('rejected', { key: 'Duplicate' }, filters).params['status']).toBe(
      'Rejected',
    );
    expect(qualityClickThrough('ageing', { key: 'OverFiveDays' }, filters)).toEqual({
      link: '/retest-queue',
      params: { project: 7 },
    });
  });
});

describe('client health helpers', () => {
  it('lists red clients first, then amber, then green', () => {
    expect(sortedClients(clients).map((c) => c.name)).toEqual([
      'Blue Harbour',
      'Cedar Labs',
      'Acme Gears',
    ]);
  });

  it('stacks open issues by severity and opens that client’s open issues of the severity', () => {
    const s = clientSeverity(clients);
    expect(s.series.map((x) => [x.key, x.values])).toEqual([
      ['Critical', [1]],
      ['Medium', [8]],
    ]);
    const filters = dashboardFiltersFromParams(convertToParamMap({}));
    const params = clientSeverityClickThrough({ key: '2', series: 'Critical' }, filters);
    expect(params['client']).toBe('2');
    expect(params['severity']).toBe('Critical');
    expect(params['status']).toContain('Reopened');
  });

  it('shows UAT and LIVE always and Other / Not set only when used', () => {
    expect(uatVsLive(clients).series.map((s) => [s.name, s.values])).toEqual([
      ['UAT', [5]],
      ['LIVE', [3]],
      ['Not set', [1]],
    ]);
    expect(closureItems(clients)).toEqual([{ key: '2', label: 'Blue Harbour (6)', value: 2.5 }]);
  });
});

describe('quality and client health pages', () => {
  beforeAll(() => {
    // jsdom has no ResizeObserver (ngx-echarts autoResize).
    globalThis.ResizeObserver ??= class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    };
  });

  function setup(): HttpTestingController {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'quality', component: QualityPage, providers: [provideCharts()] },
          { path: 'clients', component: ClientsPage, providers: [provideCharts()] },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeAuth(testUser('Tester', { id: 5 })),
      ],
    });
    return TestBed.inject(HttpTestingController);
  }

  it('shows the quality KPIs with median hours, the long-waiting alert and the rejected ratio', async () => {
    const http = setup();
    const harness = await RouterTestingHarness.create('/quality');
    http.expectOne((r) => r.url.endsWith('/analytics/quality')).flush(quality);
    http.match(() => true).forEach((r) => r.flush([]));
    await harness.fixture.whenStable();
    const el = harness.routeNativeElement!;
    const cards = [...el.querySelectorAll('app-kpi-card')].map((k) =>
      k.textContent!.replace(/\s+/g, ''),
    );
    expect(cards[0]).toContain('6.3h');
    expect(cards[1]).toContain('Intheretestqueue6');
    expect(cards[3]).toContain('7.5%');
    expect(el.querySelector('app-kpi-card.qa-attention')!.textContent).toContain('over 5 days');
  });

  it('shows a card per client with its health in words, red first', async () => {
    const http = setup();
    const harness = await RouterTestingHarness.create('/clients');
    http.expectOne((r) => r.url.endsWith('/analytics/clients')).flush(clients);
    http.match(() => true).forEach((r) => r.flush([]));
    await harness.fixture.whenStable();
    const cards = [...harness.routeNativeElement!.querySelectorAll('.qa-client-card')];
    expect(cards.map((c) => c.getAttribute('data-health'))).toEqual(['Red', 'Amber', 'Green']);
    expect(cards[0].querySelector('app-health-badge')!.textContent).toContain('Critical');
    expect(cards[0].textContent).toContain('NOVA · Sprint 3 – LIVE');
    expect(cards[0].textContent).toContain('27/Oct/2026');
    expect(cards[2].textContent).toContain('No sprint yet');
  });
});
