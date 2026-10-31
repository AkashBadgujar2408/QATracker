import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { provideFakeAuth, testUser } from '../../../testing/fake-auth';
import { Cascade } from '../../shared/data/masters.api';
import { OpenIssue, Sprint, SprintStatus } from '../../shared/data/sprints.api';
import { Role } from '../../shared/models/role';
import { CloseSprintDialog, CloseSprintDialogData, carryTargets } from './close-sprint-dialog';
import { SprintsPage, donePercent, lifecycle } from './sprints-page';

const cascade: Cascade = {
  clients: [{ id: 1, name: 'Pardi Gears' }],
  projects: [{ id: 1, name: 'ERP', key: 'PGERP' }],
  modules: [],
  environments: [],
  members: [],
};

function sprint(id: number, status: SprintStatus, overrides: Partial<Sprint> = {}): Sprint {
  return {
    id,
    projectId: 1,
    projectKey: 'PGERP',
    projectName: 'ERP',
    clientId: 1,
    clientName: 'Pardi Gears',
    name: `Sprint ${id} – UAT`,
    phaseId: 2,
    phaseName: 'UAT',
    defaultEnvironmentId: null,
    defaultEnvironmentName: null,
    status,
    startedAt: `2026-10-${10 + id}T04:30:00Z`,
    startedByName: 'John Tester',
    testingCompletedAt: status === 'Testing' ? null : `2026-10-${12 + id}T12:30:00Z`,
    testingCompletedByName: status === 'Testing' ? null : 'John Tester',
    closedAt: status === 'Closed' ? `2026-10-${14 + id}T12:30:00Z` : null,
    issueCount: 4,
    openIssueCount: 2,
    carriedForwardCount: 0,
    rowVersion: `RV${id}`,
    ...overrides,
  };
}

function open(id: number): OpenIssue {
  return {
    id,
    key: `PGERP-${id}`,
    title: `Issue ${id}`,
    moduleName: 'Sales',
    severity: 'High',
    status: 'Assigned',
    assigneeName: 'Emily Dev',
    rowVersion: `IV${id}`,
  };
}

async function settle(): Promise<void> {
  TestBed.tick();
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
}

describe('sprint helpers', () => {
  it('shows the lifecycle stops reached and the current one', () => {
    const steps = lifecycle(sprint(1, 'TestingCompleted'));
    expect(steps.map((s) => [s.label, !!s.at, s.current])).toEqual([
      ['Started', true, false],
      ['Testing completed', true, true],
      ['Closed', false, false],
    ]);
  });

  it('counts finished issues as a share, 0 for an empty sprint', () => {
    expect(donePercent(sprint(1, 'Testing', { issueCount: 4, openIssueCount: 1 }))).toBe(75);
    expect(donePercent(sprint(1, 'Testing', { issueCount: 0, openIssueCount: 0 }))).toBe(0);
  });

  it('offers other open sprints of the project as carry targets, the one in testing first', () => {
    const source = sprint(1, 'TestingCompleted');
    const targets = carryTargets(source, [
      source,
      sprint(2, 'TestingCompleted'),
      sprint(3, 'Closed'),
      sprint(4, 'Testing'),
      sprint(5, 'Testing', { projectId: 9 }),
    ]);
    expect(targets.map((s) => s.id)).toEqual([4, 2]);
  });
});

describe('CloseSprintDialog', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  function render(data: CloseSprintDialogData) {
    close.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(CloseSprintDialog);
    TestBed.tick();
    return fixture;
  }

  const source = sprint(1, 'TestingCompleted');
  const sprints = [source, sprint(2, 'Testing')];

  function button(el: HTMLElement, label: string): HTMLButtonElement {
    const found = [...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === label);
    expect(found, `button "${label}"`).toBeTruthy();
    return found!;
  }

  it('closes and carries every open issue forward in one request', async () => {
    const fixture = render({ mode: 'close', sprint: source, sprints });
    http.expectOne('/api/sprints/1/open-issues').flush([open(7), open(9)]);
    await settle();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('2 issues are still open');
    expect(el.textContent).toContain('PGERP-9');

    button(el, 'Carry forward 2 and close').click();
    const req = http.expectOne('/api/sprints/1/close');
    expect(req.request.body).toEqual({
      rowVersion: 'RV1',
      note: null,
      carryForward: {
        targetSprintId: 2,
        issues: [
          { id: 7, rowVersion: 'IV7' },
          { id: 9, rowVersion: 'IV9' },
        ],
      },
    });
    req.flush(sprint(1, 'Closed'));
    expect(close).toHaveBeenCalledWith(
      expect.objectContaining({ kind: 'closed', carried: 2, target: sprints[1] }),
    );
  });

  it('closes a finished sprint without carrying anything', async () => {
    const fixture = render({ mode: 'close', sprint: source, sprints: [source] });
    http.expectOne('/api/sprints/1/open-issues').flush([]);
    await settle();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('are closed or rejected');

    button(el, 'Close sprint').click();
    const req = http.expectOne('/api/sprints/1/close');
    expect(req.request.body.carryForward).toBeNull();
  });

  it('carries only the chosen issues and keeps the sprint open', async () => {
    const fixture = render({ mode: 'carry', sprint: source, sprints });
    http.expectOne('/api/sprints/1/open-issues').flush([open(7), open(9)]);
    await settle();
    const el = fixture.nativeElement as HTMLElement;
    expect(button(el, 'Carry forward 0 issues').disabled).toBe(true);

    el.querySelector<HTMLInputElement>('input[aria-label="Carry forward PGERP-9"]')!.click();
    await settle();
    button(el, 'Carry forward 1 issue').click();
    const req = http.expectOne('/api/sprints/1/carry-forward');
    expect(req.request.body).toEqual({
      targetSprintId: 2,
      issues: [{ id: 9, rowVersion: 'IV9' }],
      note: null,
    });
  });

  it('asks for a new sprint when there is no open one to carry to', async () => {
    const fixture = render({
      mode: 'close',
      sprint: source,
      sprints: [source, sprint(3, 'Closed')],
    });
    http.expectOne('/api/sprints/1/open-issues').flush([open(7)]);
    await settle();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('no open sprint in this project');

    button(el, 'Start new sprint').click();
    expect(close).toHaveBeenCalledWith({ kind: 'start' });
  });
});

describe('SprintsPage', () => {
  let http: HttpTestingController;

  async function render(role: Role) {
    const queryParamMap = convertToParamMap({ client: '1', project: '1' });
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeAuth(testUser(role)),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap }, queryParamMap: of(queryParamMap) },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(SprintsPage);
    TestBed.tick();
    http.expectOne((r) => r.url === '/api/lookups/cascade').flush(cascade);
    http
      .expectOne('/api/projects/1/sprints')
      .flush([
        sprint(3, 'Testing'),
        sprint(2, 'TestingCompleted'),
        sprint(1, 'Closed', { issueCount: 0, openIssueCount: 0, carriedForwardCount: 4 }),
      ]);
    await settle();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('lists the sprints with the next step beside each for a tester', async () => {
    const el = await render('Tester');
    const cards = el.querySelectorAll('li.qa-sprint');
    expect(cards.length).toBe(3);
    expect(cards[0].textContent).toContain('Mark testing completed');
    expect(cards[1].textContent).toContain('Close sprint');
    expect(cards[1].textContent).toContain('2 of 4 closed or rejected');
    expect(cards[2].textContent).not.toContain('Close sprint');
    expect(cards[2].textContent).toContain('All 4 issues carried forward');
    expect(cards[0].querySelector('.qa-track')!.textContent).toContain('Not yet');
    expect(cards[2].querySelector('.qa-track')!.textContent).not.toContain('Not yet');
  });

  it('gives developers no lifecycle actions', async () => {
    const el = await render('Developer');
    expect(el.textContent).not.toContain('Mark testing completed');
    expect(el.textContent).not.toContain('Close sprint');
    expect(el.textContent).toContain('Download report');
  });

  it('opens the sprint history on demand', async () => {
    const el = await render('Tester');
    const history = [...el.querySelectorAll('li.qa-sprint')][2].querySelector<HTMLButtonElement>(
      'button[aria-controls="history-1"]',
    )!;
    history.click();
    await settle();
    http.expectOne('/api/sprints/1').flush({
      sprint: sprint(1, 'Closed'),
      events: [
        { kind: 'Started', userName: 'John Tester', note: null, at: '2026-10-11T04:30:00Z' },
        {
          kind: 'IssuesCarriedForward',
          userName: 'John Tester',
          note: '2 issues carried forward to Sprint 2 – UAT: PGERP-7, PGERP-9.',
          at: '2026-10-15T12:00:00Z',
        },
        { kind: 'Closed', userName: 'John Tester', note: null, at: '2026-10-15T12:30:00Z' },
      ],
    });
    await settle();
    const text = el.querySelector('#history-1')!.textContent!;
    expect(text).toContain('John Tester carried issues forward');
    expect(text).toContain('PGERP-7, PGERP-9');
    expect(text).toContain('John Tester closed the sprint');
  });
});
