import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideFakeAuth, testUser } from '../../../testing/fake-auth';
import { Issue, IssueListItem, PagedResult } from '../../shared/data/issues.api';
import { Cascade } from '../../shared/data/masters.api';
import { Sprint, SprintStatus } from '../../shared/data/sprints.api';
import { isBlankHtml } from '../../shared/ui/rich-text-editor/rich-text-editor';
import { IssueForm } from './issue-form';
import { TestingPage, sprintStats } from './testing-page';

const cascade: Cascade = {
  clients: [{ id: 1, name: 'Pardi Gears' }],
  projects: [{ id: 1, name: 'ERP', key: 'PGERP' }],
  modules: [{ id: 7, name: 'Sales Return' }],
  environments: [{ id: 3, name: 'UAT 1', type: 'UAT', url: 'https://uat.example.test' }],
  members: [
    { userId: 5, fullName: 'Emily Dev', role: 'Developer' },
    { userId: 6, fullName: 'John Tester', role: 'Tester' },
  ],
};

function sprint(status: SprintStatus, overrides: Partial<Sprint> = {}): Sprint {
  return {
    id: 1,
    projectId: 1,
    projectKey: 'PGERP',
    projectName: 'ERP',
    clientId: 1,
    clientName: 'Pardi Gears',
    name: 'Sprint 1 – UAT',
    phaseId: 2,
    phaseName: 'UAT',
    defaultEnvironmentId: 3,
    defaultEnvironmentName: 'UAT 1',
    status,
    startedAt: '2026-10-27T04:30:00Z',
    startedByName: 'John Tester',
    testingCompletedAt: status === 'Testing' ? null : '2026-10-28T12:30:00Z',
    testingCompletedByName: status === 'Testing' ? null : 'John Tester',
    closedAt: null,
    issueCount: 1,
    openIssueCount: 1,
    rowVersion: 'AAAAAAAAB9E=',
    ...overrides,
  };
}

function listItem(overrides: Partial<IssueListItem> = {}): IssueListItem {
  return {
    id: 1,
    key: 'PGERP-1',
    title: 'Return quantity accepts negative values',
    projectId: 1,
    projectKey: 'PGERP',
    sprintId: 1,
    sprintName: 'Sprint 1 – UAT',
    moduleName: 'Sales Return',
    screenName: 'Sales Return Entry',
    type: 'Validation',
    severity: 'High',
    priority: 'High',
    status: 'New',
    assigneeId: null,
    assigneeName: null,
    reporterName: 'John Tester',
    dueDate: null,
    createdAt: '2026-10-27T05:00:00Z',
    updatedAt: null,
    ...overrides,
  };
}

function page(items: IssueListItem[]): PagedResult<IssueListItem> {
  return { items, page: 1, pageSize: 200, totalCount: items.length };
}

/** Lets resources react to flushed responses (their requests start in effects). */
async function settle(): Promise<void> {
  TestBed.tick();
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
}

describe('sprintStats', () => {
  const now = new Date('2026-10-28T10:00:00Z'); // 28/Oct 15:30 IST

  it('counts total, today (IST) and severities in importance order', () => {
    const stats = sprintStats(
      [
        listItem({ id: 1, severity: 'Low', createdAt: '2026-10-28T09:00:00Z' }),
        listItem({ id: 2, severity: 'Critical', createdAt: '2026-10-27T19:00:00Z' }), // 28/Oct 00:30 IST
        listItem({ id: 3, severity: 'Low', createdAt: '2026-10-27T05:00:00Z' }),
      ],
      true,
      now,
    );
    expect(stats.total).toBe(3);
    expect(stats.today).toBe(2);
    expect(stats.bySeverity).toEqual([
      { severity: 'Critical', count: 1 },
      { severity: 'Low', count: 2 },
    ]);
  });

  it('counts days on which issues were logged, plus today while testing', () => {
    const issues = [listItem({ createdAt: '2026-10-26T05:00:00Z' })];
    expect(sprintStats(issues, true, now).day).toBe(2);
    expect(sprintStats(issues, false, now).day).toBe(1);
    expect(sprintStats([], true, now).day).toBe(1);
  });
});

describe('isBlankHtml', () => {
  it('treats empty editor markup as blank', () => {
    expect(isBlankHtml(null)).toBe(true);
    expect(isBlankHtml('<br>')).toBe(true);
    expect(isBlankHtml('<p>&nbsp;</p>')).toBe(true);
    expect(isBlankHtml('<p>Saves with a <b>future</b> date</p>')).toBe(false);
  });
});

describe('TestingPage', () => {
  let http: HttpTestingController;

  function render(params: Record<string, string> = {}) {
    try {
      localStorage.clear();
    } catch {
      // ignore
    }
    const queryParamMap = convertToParamMap(params);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeAuth(testUser('Tester')),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap } } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TestingPage);
    TestBed.tick();
    return fixture;
  }

  async function loadSprint(status: SprintStatus) {
    const fixture = render({ client: '1', project: '1', sprint: '1' });
    http.expectOne((r) => r.url === '/api/lookups/cascade').flush(cascade);
    http.expectOne('/api/projects/1/sprints').flush([sprint(status)]);
    await settle();
    http
      .match((r) => r.url === '/api/projects/1/screens')
      .forEach((r) => r.flush(['Sales Return Entry']));
    http.expectOne((r) => r.url === '/api/issues').flush(page([listItem()]));
    await fixture.whenStable();
    return fixture;
  }

  function buttons(fixture: { nativeElement: HTMLElement }, label: string): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('button')).filter((b) =>
      b.textContent?.trim().startsWith(label),
    );
  }

  it('asks for client and project first', async () => {
    const fixture = render();
    http.expectOne((r) => r.url === '/api/lookups/cascade').flush(cascade);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Choose what you are testing',
    );
  });

  it('offers to start a sprint when the project has none', async () => {
    const fixture = render({ client: '1', project: '1' });
    http.expectOne((r) => r.url === '/api/lookups/cascade').flush(cascade);
    http.expectOne('/api/projects/1/sprints').flush([]);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'No sprint in this project yet',
    );
  });

  it('logs issues while the sprint is in testing', async () => {
    const fixture = await loadSprint('Testing');
    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(text).toContain('PGERP-1');
    expect(text).toContain('Day');
    expect(buttons(fixture, 'Mark testing completed')).toHaveLength(1);
    expect(buttons(fixture, 'Save & add another')[0].disabled).toBe(false);
  });

  it('is read-only once testing is completed', async () => {
    const fixture = await loadSprint('TestingCompleted');
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('New defects go into a new sprint');
    expect(buttons(fixture, 'Mark testing completed')).toHaveLength(0);
    expect(buttons(fixture, 'Save & add another')[0].disabled).toBe(true);
    expect(element.querySelector('[aria-label="Edit PGERP-1"]')).toBeNull();
    expect(element.querySelector('[aria-label="Delete PGERP-1"]')).toBeNull();
    // No Testing sprint left: offer the next one.
    expect(buttons(fixture, 'Start new sprint').length).toBeGreaterThan(0);
  });
});

describe('IssueForm', () => {
  let http: HttpTestingController;

  it('saves, then keeps module, screen and type for the next issue', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(IssueForm);
    fixture.componentRef.setInput('sprint', sprint('Testing'));
    fixture.componentRef.setInput('cascade', cascade);
    TestBed.tick();
    http.match((r) => r.url === '/api/projects/1/screens').forEach((r) => r.flush([]));

    const form = fixture.componentInstance['form'];
    expect(form.controls.foundOnEnvironmentId.value).toBe(3); // sprint default
    form.patchValue({
      moduleId: 7,
      screenName: 'Sales Return Entry',
      type: 'Validation',
      title: 'Return date accepts a future date',
      actualResult: '<p>Saves with a future date</p>',
      severity: 'High',
      priority: 'Medium',
      assigneeId: 5,
    });
    const saved = vi.fn();
    fixture.componentInstance.saved.subscribe(saved);
    fixture.componentInstance['save'](true);

    const request = http.expectOne('/api/issues');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toMatchObject({
      sprintId: 1,
      moduleId: 7,
      screenName: 'Sales Return Entry',
      foundOnEnvironmentId: 3,
      type: 'Validation',
      severity: 'High',
      priority: 'Medium',
      assigneeId: 5,
      pageUrl: null,
    });
    request.flush({ id: 9, key: 'PGERP-9' } as Issue);

    expect(saved).toHaveBeenCalledWith({ issue: { id: 9, key: 'PGERP-9' }, created: true });
    expect(form.getRawValue()).toMatchObject({
      moduleId: 7,
      screenName: 'Sales Return Entry',
      type: 'Validation',
      foundOnEnvironmentId: 3,
      title: '',
      actualResult: null,
      severity: null,
      priority: null,
      assigneeId: null,
    });
  });

  it('does not send an incomplete issue', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(IssueForm);
    fixture.componentRef.setInput('sprint', sprint('Testing'));
    fixture.componentRef.setInput('cascade', cascade);
    TestBed.tick();
    http.match((r) => r.url === '/api/projects/1/screens').forEach((r) => r.flush([]));

    fixture.componentInstance['save'](true);
    http.expectNone('/api/issues');
    expect(fixture.componentInstance['error']()).toBe('Fill in the highlighted fields.');
  });
});
