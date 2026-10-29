import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { convertToParamMap, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideFakeAuth, testUser } from '../../../testing/fake-auth';
import { ToastService } from '../../core/feedback/toast.service';
import { Board, BoardCard } from '../../shared/data/board.api';
import { ISSUE_STATUSES } from '../../shared/models/issue-enums';
import {
  boardFiltersFromParams,
  boardFiltersToParams,
  boardQuery,
  moveFor,
  refusal,
  visibleColumns,
} from './board-logic';
import { BoardPage } from './board-page';

function card(overrides: Partial<BoardCard> = {}): BoardCard {
  return {
    id: 1,
    key: 'PGERP-1',
    title: 'Return quantity accepts negative values',
    sprintId: 1,
    sprintName: 'Sprint 1 – UAT',
    sprintStatus: 'TestingCompleted',
    moduleName: 'Sales Return',
    screenName: 'Entry',
    severity: 'High',
    priority: 'Medium',
    status: 'New',
    assigneeId: null,
    assigneeName: null,
    assigneeAvatarColor: null,
    dueDate: null,
    overdue: false,
    reopenCount: 0,
    rowVersion: 'AAAAAAAAB9E=',
    moves: [{ action: 'Pick', target: 'InProgress', requires: [] }],
    ...overrides,
  };
}

function board(...cards: BoardCard[]): Board {
  const order = [
    'New',
    'Reopened',
    'Assigned',
    'InProgress',
    'NeedInfo',
    'OnHold',
    'Disputed',
    'Fixed',
    'Verified',
    'Closed',
    'Rejected',
  ] as const;
  return {
    projectId: 7,
    columns: order.map((status) => {
      const inColumn = cards.filter((c) => c.status === status);
      return { status, count: inColumn.length, cards: inColumn };
    }),
  };
}

describe('board logic', () => {
  it('reads and writes the filters of the URL', () => {
    const f = boardFiltersFromParams(
      convertToParamMap({
        client: '2',
        project: '7',
        who: 'me',
        severity: 'Critical,Bogus',
        done: '1',
      }),
    );
    expect(f).toEqual({
      clientId: 2,
      projectId: 7,
      sprintId: null,
      who: 'me',
      severity: ['Critical'],
      q: '',
      done: true,
    });
    expect(boardFiltersToParams(f)).toEqual({
      client: 2,
      project: 7,
      sprint: null,
      who: 'me',
      severity: 'Critical',
      q: null,
      done: '1',
    });
    expect(boardQuery({ ...f, projectId: 7, who: 12 })).toMatchObject({
      assigneeId: 12,
      mine: false,
      unassigned: false,
    });
  });

  it('accepts only the listed moves and explains refusals', () => {
    const c = card({
      status: 'InProgress',
      moves: [
        { action: 'NeedInfo', target: 'NeedInfo', requires: ['reason'] },
        { action: 'MarkFixed', target: 'Fixed', requires: ['reason', 'environments'] },
      ],
    });
    expect(moveFor(c, 'Fixed')?.action).toBe('MarkFixed');
    expect(moveFor(c, 'Closed')).toBeUndefined();
    expect(refusal(c, 'Closed', 'Developer')).toBe(
      "PGERP-1 can't go from In Progress to Closed. It can go to Need Info or Fixed.",
    );
    expect(
      refusal(card({ sprintStatus: 'Testing', moves: [] }), 'InProgress', 'Developer'),
    ).toContain('Testing of Sprint 1 – UAT is still in progress');
    expect(refusal(card({ sprintStatus: 'Closed' }), 'Assigned', 'Admin')).toContain('read-only');
    expect(refusal(card({ moves: [] }), 'Fixed', 'Tester')).toContain('nothing you can move it to');
  });

  it('hides Closed and Rejected unless asked and shows a pending move in its new column', () => {
    const b = board(card(), card({ id: 2, key: 'PGERP-2', status: 'Closed', moves: [] }));
    expect(visibleColumns(b, false, null).map((c) => c.status)).not.toContain('Closed');
    expect(visibleColumns(b, true, null)).toHaveLength(ISSUE_STATUSES.length);

    const moved = visibleColumns(b, false, { key: 'PGERP-1', to: 'InProgress' });
    expect(moved.find((c) => c.status === 'New')!.cards).toHaveLength(0);
    const inProgress = moved.find((c) => c.status === 'InProgress')!;
    expect(inProgress.count).toBe(1);
    expect(inProgress.cards[0].key).toBe('PGERP-1');
  });
});

describe('BoardPage', () => {
  let http: HttpTestingController;

  async function open(url: string) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'board', component: BoardPage }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeAuth(testUser('Developer')),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create(url);
    return harness;
  }

  function flushLookups(): void {
    http
      .match((r) => r.url.includes('/lookups/cascade'))
      .forEach((r) =>
        r.flush({
          clients: [{ id: 2, name: 'Acme' }],
          projects: [{ id: 7, name: 'ERP', key: 'PGERP' }],
          modules: [],
          environments: [],
          members: [],
        }),
      );
    http
      .match((r) => r.url.endsWith('/sprints') || r.url.includes('/projects/7/sprints'))
      .forEach((r) => r.flush([]));
    http.match((r) => r.url.includes('/lookups/users')).forEach((r) => r.flush([]));
  }

  afterEach(() => localStorage.clear());

  it('asks for a project first', async () => {
    const harness = await open('/board');
    flushLookups();
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.textContent).toContain('Choose a project');
  });

  it('shows the columns with counts and runs a move from the card menu', async () => {
    const harness = await open('/board?client=2&project=7');
    flushLookups();
    http
      .expectOne((r) => r.url.endsWith('/board') && r.params.get('projectId') === '7')
      .flush(board(card()));
    await harness.fixture.whenStable();

    const el = harness.routeNativeElement!;
    const lanes = [...el.querySelectorAll('section.qa-lane')];
    expect(lanes.map((l) => l.querySelector('h2')!.textContent!.trim())).toEqual([
      'New',
      'Reopened',
      'Assigned',
      'In Progress',
      'Need Info',
      'On Hold',
      'Disputed',
      'Fixed',
      'Verified',
    ]);
    expect(lanes[0].textContent).toContain('PGERP-1');

    const page = harness.routeDebugElement!.componentInstance as BoardPage;
    void (
      page as unknown as { run(c: BoardCard, m: BoardCard['moves'][number]): Promise<void> }
    ).run(card(), card().moves[0]);
    http
      .expectOne((r) => r.url.endsWith('/issues/PGERP-1/pick'))
      .flush({ key: 'PGERP-1', status: 'InProgress' });
    TestBed.tick();
    http
      .expectOne((r) => r.url.endsWith('/board'))
      .flush(board(card({ status: 'InProgress', moves: [] })));
    await harness.fixture.whenStable();
    expect(el.querySelectorAll('section.qa-lane')[3].textContent).toContain('PGERP-1');
  });

  it('refuses a drop on a column the card cannot go to', async () => {
    const harness = await open('/board?client=2&project=7');
    flushLookups();
    http.expectOne((r) => r.url.endsWith('/board')).flush(board(card()));
    await harness.fixture.whenStable();
    const toast = TestBed.inject(ToastService);
    const warn = vi.spyOn(toast, 'warn');

    const page = harness.routeDebugElement!.componentInstance as unknown as {
      dropped(e: unknown): void;
    };
    page.dropped({ item: { data: card() }, container: { data: 'Fixed' } });
    expect(warn).toHaveBeenCalledWith(
      "PGERP-1 can't go from New to Fixed. It can go to In Progress.",
    );
    http.expectNone((r) => r.method === 'POST');
  });
});
