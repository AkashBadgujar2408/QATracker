import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { provideFakeAuth, testUser } from '../../../testing/fake-auth';
import { Deployment, Issue } from '../../shared/data/issues.api';
import { mentionQuery } from '../../shared/timeline/comment-composer';
import { commentSegments, relativeTime } from '../../shared/timeline/issue-timeline';
import {
  EMPTY_FILTERS,
  activeFilterCount,
  filtersFromParams,
  filtersToParams,
  filtersToQuery,
} from './issue-filters';
import { IssueDetailPage, actionLabel, environmentStrip } from './issue-detail-page';

function deployment(overrides: Partial<Deployment>): Deployment {
  return {
    id: 1,
    environmentId: 3,
    environmentName: 'UAT 1',
    environmentType: 'UAT',
    environmentUrl: 'https://uat.example.test',
    deployedByName: 'Emily Developer',
    deployedAt: '2026-10-28T12:00:00Z',
    note: null,
    version: null,
    retestResult: 'Pending',
    retestedByName: null,
    retestedAt: null,
    retestComment: null,
    ...overrides,
  };
}

function issue(overrides: Partial<Issue> = {}): Issue {
  return {
    id: 1,
    key: 'PGERP-1',
    number: 1,
    clientId: 1,
    clientName: 'Pardi Gears',
    projectId: 1,
    projectKey: 'PGERP',
    projectName: 'ERP',
    sprintId: 1,
    sprintName: 'Sprint 1 – UAT',
    sprintStatus: 'TestingCompleted',
    moduleId: 7,
    moduleName: 'Sales Return',
    screenName: 'Sales Return Entry',
    foundOnEnvironmentId: 3,
    foundOnEnvironmentName: 'UAT 1',
    pageUrl: null,
    type: 'Bug',
    title: 'Return quantity accepts negative values',
    steps: '<ol><li>Open</li></ol>',
    expectedResult: null,
    actualResult: '<p>It saves.</p>',
    impact: null,
    severity: 'High',
    priority: 'High',
    status: 'New',
    reporterId: 1,
    reporterName: 'John Tester',
    assigneeId: null,
    assigneeName: null,
    dueDate: null,
    reopenCount: 0,
    createdAt: '2026-10-27T05:00:00Z',
    updatedAt: null,
    rowVersion: 'AAAAAAAAB9E=',
    permissions: { canEdit: false, canDelete: false },
    deployments: [],
    allowedActions: [],
    ...overrides,
  };
}

describe('issue filters', () => {
  it('round-trips through the URL and leaves defaults out', () => {
    const filters = {
      ...EMPTY_FILTERS,
      projectId: 4,
      status: ['New', 'Reopened'] as const,
      assignee: 'none' as const,
      overdue: true,
      q: 'discount',
      page: 2,
    };
    const params = filtersToParams(filters);
    expect(params).toMatchObject({
      project: 4,
      status: 'New,Reopened',
      assignee: 'none',
      overdue: '1',
      page: 2,
    });
    expect(params['sort']).toBeNull();

    const back = filtersFromParams(
      convertToParamMap(
        Object.fromEntries(
          Object.entries(params)
            .filter(([, v]) => v !== null)
            .map(([k, v]) => [k, String(v)]),
        ),
      ),
    );
    expect(back).toEqual(filters);
    expect(activeFilterCount(back)).toBe(6);
  });

  it('ignores unknown values and maps to the API query', () => {
    const filters = filtersFromParams(
      convertToParamMap({ status: 'New,Bogus', severity: 'High', size: '999', page: '-3' }),
    );
    expect(filters.status).toEqual(['New']);
    expect(filters.size).toBe(25);
    expect(filters.page).toBe(1);
    expect(filtersToQuery({ ...filters, assignee: 'none' })).toMatchObject({
      status: ['New'],
      severity: ['High'],
      unassigned: true,
      assigneeId: undefined,
    });
  });
});

describe('issue detail helpers', () => {
  it('labels actions for the situation', () => {
    expect(actionLabel('Assign', { status: 'New', assigneeId: null })).toBe('Assign');
    expect(actionLabel('Assign', { status: 'Assigned', assigneeId: 5 })).toBe('Reassign');
    expect(actionLabel('MarkFixed', { status: 'InProgress', assigneeId: 5 })).toBe('Mark fixed');
    expect(actionLabel('MarkFixed', { status: 'Verified', assigneeId: 5 })).toBe('Add deployment');
  });

  it('shows the latest deployment per environment', () => {
    const strip = environmentStrip([
      deployment({ id: 1, retestResult: 'Failed' }),
      deployment({ id: 2, environmentId: 9, environmentName: 'LIVE', environmentType: 'LIVE' }),
      deployment({ id: 3, retestResult: 'Passed' }),
    ]);
    expect(strip.map((e) => [e.name, e.latest.id, e.latest.retestResult])).toEqual([
      ['UAT 1', 3, 'Passed'],
      ['LIVE', 2, 'Pending'],
    ]);
  });
});

describe('timeline helpers', () => {
  it('finds the @mention being typed', () => {
    expect(mentionQuery('Ping @emi', 9)).toEqual({ start: 5, query: 'emi' });
    expect(mentionQuery('@', 1)).toEqual({ start: 0, query: '' });
    expect(mentionQuery('mail john@exa', 13)).toBeNull();
  });

  it('highlights only real mentions', () => {
    const parts = commentSegments('Over to @emily.dev, not @someone.', [
      { userId: 5, userName: 'emily.dev', fullName: 'Emily Developer' },
    ]);
    expect(parts).toEqual([
      { text: 'Over to ', mention: false },
      { text: '@emily.dev', mention: true },
      { text: ', not @someone.', mention: false },
    ]);
  });

  it('shows relative times, then the IST date', () => {
    const now = new Date('2026-10-28T12:00:00Z');
    expect(relativeTime('2026-10-28T11:59:40Z', now)).toBe('just now');
    expect(relativeTime('2026-10-28T11:15:00Z', now)).toBe('45 min ago');
    expect(relativeTime('2026-10-28T09:00:00Z', now)).toBe('3 h ago');
    expect(relativeTime('2026-10-26T09:00:00Z', now)).toBe('26/Oct/2026 14:30');
  });
});

describe('IssueDetailPage', () => {
  let http: HttpTestingController;

  async function render(loaded: Issue, role: 'Developer' | 'Tester' = 'Developer') {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeAuth(testUser(role)),
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of(convertToParamMap({ key: loaded.key })),
            snapshot: { paramMap: convertToParamMap({ key: loaded.key }) },
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(IssueDetailPage);
    TestBed.tick();
    http.expectOne(`/api/issues/${loaded.key}`).flush(loaded);
    http
      .match(() => true)
      .forEach((r) => {
        if (r.request.url.endsWith('/activities')) {
          r.flush({ items: [], hasMore: false });
        } else if (
          r.request.url.endsWith('/related') ||
          r.request.url.endsWith('/attachments') ||
          r.request.url.endsWith('/users')
        ) {
          r.flush([]);
        } else if (r.request.url.endsWith('/cascade')) {
          r.flush({ clients: [], projects: [], modules: [], environments: [], members: [] });
        }
      });
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
    http
      .match(() => true)
      .forEach((r) =>
        r.flush(r.request.url.endsWith('/activities') ? { items: [], hasMore: false } : []),
      );
    await fixture.whenStable();
    return fixture;
  }

  function buttonTexts(fixture: { nativeElement: HTMLElement }): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll('[aria-label="Actions"] button')).map(
      (b) => (b.textContent ?? '').trim(),
    );
  }

  it('offers exactly the API allowedActions and picks with one click', async () => {
    const fixture = await render(
      issue({
        allowedActions: [
          { action: 'Assign', target: 'Assigned', requires: ['assignee'] },
          { action: 'Pick', target: 'InProgress', requires: [] },
        ],
      }),
    );
    expect(buttonTexts(fixture)).toEqual(['Assign', 'Pick']);

    const pick = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Pick',
    )!;
    pick.click();
    const request = http.expectOne('/api/issues/PGERP-1/pick');
    expect(request.request.method).toBe('POST');
  });

  it('tells developers that testing is still in progress', async () => {
    const fixture = await render(issue({ sprintStatus: 'Testing' }));
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Testing in progress');
    expect(buttonTexts(fixture)).toEqual([]);
  });

  it('puts Pass / Fail on a pending retest instead of Verify / Reopen', async () => {
    const fixture = await render(
      issue({
        status: 'Fixed',
        assigneeId: 5,
        assigneeName: 'Emily Developer',
        deployments: [deployment({})],
        allowedActions: [
          { action: 'Assign', target: 'Fixed', requires: ['assignee'] },
          { action: 'Verify', target: 'Verified', requires: [] },
          { action: 'Reopen', target: 'Reopened', requires: ['reason'] },
        ],
      }),
      'Tester',
    );
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(buttonTexts(fixture)).toEqual(['Reassign']);
    expect(text).toContain('Awaiting retest');
    expect(text).toContain('Pass');
    expect(text).toContain('Fail');
  });
});
