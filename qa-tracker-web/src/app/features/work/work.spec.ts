import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { provideFakeAuth, testUser } from '../../../testing/fake-auth';
import {
  DeployCandidate,
  MyWork,
  RetestQueue,
  WorkItem,
  WorkQueue,
  waitBucket,
  waitLabel,
  waitingDays,
} from '../../shared/data/work.api';
import { blockedReason, groupByProject } from './deploy-notice-page';
import { MyWorkPage, tabFrom } from './my-work-page';
import { RetestQueuePage } from './retest-queue-page';
import { isOverdue } from './work-item-row';

function item(overrides: Partial<WorkItem> = {}): WorkItem {
  return {
    id: 1,
    key: 'PGERP-1',
    title: 'Return quantity accepts negative values',
    projectId: 1,
    projectKey: 'PGERP',
    projectName: 'ERP',
    clientName: 'Acme Gears',
    sprintId: 1,
    sprintName: 'Sprint 1 – UAT',
    moduleName: 'Sales Return',
    screenName: 'Entry',
    severity: 'High',
    priority: 'Medium',
    status: 'Assigned',
    assigneeId: 5,
    assigneeName: 'Emily Developer',
    reporterName: 'John Tester',
    dueDate: null,
    reopenCount: 0,
    createdAt: '2026-10-27T10:00:00Z',
    updatedAt: null,
    rowVersion: 'AAAAAAAAB9E=',
    ...overrides,
  };
}

const queue = (...items: WorkItem[]): WorkQueue => ({ count: items.length, items });

function candidate(
  overrides: Partial<WorkItem>,
  deployments: DeployCandidate['deployments'] = [],
): DeployCandidate {
  return { issue: item({ status: 'Fixed', ...overrides }), deployments };
}

describe('work queue helpers', () => {
  it('buckets the wait like the plan (< 1 day, 1–2, 3–5, > 5)', () => {
    const now = new Date('2026-10-28T12:00:00Z');
    expect(waitingDays('2026-10-28T09:00:00Z', now)).toBe(0);
    expect(waitingDays('2026-10-25T11:00:00Z', now)).toBe(3);
    expect([0, 1, 2, 3, 5, 6].map(waitBucket)).toEqual([
      'fresh',
      'days',
      'days',
      'aging',
      'aging',
      'stale',
    ]);
    expect([0, 1, 4].map(waitLabel)).toEqual(['Today', '1 day', '4 days']);
  });

  it('reads the tab from the URL and flags overdue issues', () => {
    expect(tabFrom('reopened')).toBe('reopened');
    expect(tabFrom('nonsense')).toBe('assigned');
    expect(tabFrom(null)).toBe('assigned');
    const now = new Date('2026-10-28T12:00:00Z');
    expect(isOverdue(item({ dueDate: '2026-10-27T12:00:00Z' }), now)).toBe(true);
    expect(isOverdue(item({ dueDate: '2026-10-27T12:00:00Z', status: 'Fixed' }), now)).toBe(false);
    expect(isOverdue(item({ dueDate: null }), now)).toBe(false);
  });

  it('groups deploy candidates by project and blocks other projects and pending environments', () => {
    const erp = candidate({ id: 1, key: 'PGERP-1' }, [
      {
        environmentId: 3,
        environmentName: 'UAT 1',
        environmentType: 'UAT',
        retestResult: 'Pending',
        deployedAt: '2026-10-28T10:00:00Z',
      },
    ]);
    const erp2 = candidate({ id: 2, key: 'PGERP-2', status: 'Verified' });
    const crm = candidate({
      id: 3,
      key: 'CRM-1',
      projectId: 2,
      projectKey: 'CRM',
      projectName: 'CRM',
    });
    const groups = groupByProject([erp, erp2, crm]);
    expect(groups.map((g) => g.candidates.map((c) => c.issue.key))).toEqual([
      ['PGERP-1', 'PGERP-2'],
      ['CRM-1'],
    ]);
    expect(groups[0].label).toBe('Acme Gears › ERP (PGERP)');

    expect(blockedReason(crm, 1, null)).toContain('Another project');
    expect(blockedReason(erp, 1, { id: 3, name: 'UAT 1' })).toBe(
      'Already waiting for a retest on UAT 1.',
    );
    expect(blockedReason(erp, 1, { id: 4, name: 'LIVE' })).toBeNull();
    expect(blockedReason(erp2, null, null)).toBeNull();
  });
});

describe('work pages', () => {
  let http: HttpTestingController;

  async function render<T>(
    component: Type<T>,
    role: 'Developer' | 'Tester',
    query: Record<string, string> = {},
  ) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeAuth(testUser(role)),
        {
          provide: ActivatedRoute,
          useValue: {
            queryParamMap: of(convertToParamMap(query)),
            snapshot: { queryParamMap: convertToParamMap(query) },
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(component);
    TestBed.tick();
    return fixture;
  }

  async function settle(fixture: ComponentFixture<unknown>) {
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
    await fixture.whenStable();
  }

  const text = (fixture: ComponentFixture<unknown>) =>
    (fixture.nativeElement as HTMLElement).textContent ?? '';
  const button = (fixture: ComponentFixture<unknown>, label: string) =>
    Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === label,
    )!;

  it('My work shows counts on every tab and picks from Available with one click', async () => {
    const fixture = await render(MyWorkPage, 'Developer', { tab: 'available' });
    const work: MyWork = {
      assigned: queue(item()),
      available: queue(
        item({
          id: 2,
          key: 'PGERP-2',
          title: 'Totals round wrongly',
          assigneeId: null,
          assigneeName: null,
          status: 'New',
        }),
      ),
      needInfoAnswered: queue(),
      reopened: queue(),
      overdue: queue(item({ dueDate: '2020-01-01T00:00:00Z' })),
    };
    http.expectOne('/api/me/work').flush(work);
    await settle(fixture);

    const tabs = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('[role="tab"]'),
    ).map((t) => (t.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(tabs).toEqual([
      'Assigned to me 1',
      'Available to pick 1',
      'Need info answered 0',
      'Reopened 0',
      'Overdue 1',
    ]);
    expect(text(fixture)).toContain('Totals round wrongly');

    button(fixture, 'Pick').click();
    const pick = http.expectOne('/api/issues/PGERP-2/pick');
    expect(pick.request.method).toBe('POST');
  });

  it('Retest queue groups by environment with an Open button and Pass / Fail', async () => {
    const fixture = await render(RetestQueuePage, 'Tester');
    const data: RetestQueue = {
      count: 1,
      groups: [
        {
          environmentId: 3,
          environmentName: 'UAT 1',
          environmentType: 'UAT',
          environmentUrl: 'https://uat.example.test',
          projectId: 1,
          projectKey: 'PGERP',
          projectName: 'ERP',
          clientName: 'Acme Gears',
          oldestDeployedAt: '2026-10-28T10:00:00Z',
          items: [
            {
              deploymentId: 41,
              issueId: 1,
              issueKey: 'PGERP-1',
              title: 'Return quantity accepts negative values',
              moduleName: 'Sales Return',
              screenName: null,
              pageUrl: null,
              severity: 'High',
              priority: 'Medium',
              reopenCount: 1,
              reporterId: 9,
              reporterName: 'John Tester',
              assigneeName: 'Emily Developer',
              deployedByName: 'Emily Developer',
              deployedAt: '2026-10-28T10:00:00Z',
              note: 'min=0 on UI and API',
              version: '1.8.1',
              canRetest: true,
            },
          ],
        },
      ],
    };
    http.expectOne((r) => r.url === '/api/retest-queue').flush(data);
    await settle(fixture);

    const open = (fixture.nativeElement as HTMLElement).querySelector(
      'a[target="_blank"]',
    ) as HTMLAnchorElement;
    expect(open.href).toBe('https://uat.example.test/');
    expect(open.textContent).toContain('Open UAT 1');
    expect(text(fixture)).toContain('min=0 on UI and API');

    button(fixture, 'Pass').click();
    const retest = http.expectOne('/api/deployments/41/retest');
    expect(retest.request.body).toEqual({ passed: true, comment: null });
  });

  it('Retest queue without fixes says what fills it', async () => {
    const fixture = await render(RetestQueuePage, 'Tester', { mine: '1' });
    const request = http.expectOne((r) => r.url === '/api/retest-queue');
    expect(request.request.params.get('mine')).toBe('true');
    request.flush({ count: 0, groups: [] });
    await settle(fixture);
    expect(text(fixture)).toContain('No retests for your issues');
  });
});
