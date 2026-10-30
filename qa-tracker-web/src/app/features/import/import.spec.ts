import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideFakeAuth, testUser } from '../../../testing/fake-auth';
import { ImportPreview, ImportRow } from '../../shared/data/import.api';
import { buildCorrections, initialEdit, remainingWarnings } from './import-edits';
import { ImportPage, displayStatus, workbookProblem } from './import-page';

function row(overrides: Partial<ImportRow> = {}): ImportRow {
  return {
    id: '1-3',
    sheetIndex: 0,
    sheet: 'Sales & Distribution',
    row: 3,
    module: 'Sales & Distribution',
    title: 'Invoice total is wrong',
    steps: null,
    actualResult: 'Shows 0',
    expectedResult: null,
    impact: null,
    severity: 'High',
    priority: 'Medium',
    assigneeId: null,
    assigneeName: null,
    resolveBy: null,
    status: 'New',
    holdReason: null,
    rejectType: null,
    comments: [],
    images: 0,
    warnings: [],
    ...overrides,
  };
}

const preview: ImportPreview = {
  sprintId: 3,
  sprintName: 'Sprint 1 – UAT',
  projectKey: 'PGERP',
  modules: [
    {
      sheetIndex: 0,
      sheet: 'Sales & Distribution',
      name: 'Sales & Distribution',
      existingModuleId: null,
      issues: 2,
    },
  ],
  rows: [
    row({ images: 2, comments: ['For your refer.'] }),
    row({
      id: '1-4',
      row: 4,
      title: 'Discount line ignored',
      severity: 'NotAssessed',
      resolveBy: 'Priya',
      warnings: [
        'Severity not assessed in the workbook – set to Not assessed.',
        'Resolve By "Priya" matches no active developer – left unassigned.',
      ],
    }),
  ],
  readyRows: 1,
  warnings: [],
};

describe('import edits', () => {
  it('sends only what changed: skipped rows, edited fields, renamed modules', () => {
    const edits = new Map([
      ['1-3', { ...initialEdit(preview.rows[0]), include: false }],
      [
        '1-4',
        {
          ...initialEdit(preview.rows[1]),
          title: ' Discount ignored ',
          severity: 'High' as const,
          assigneeId: 5,
        },
      ],
    ]);

    expect(buildCorrections(preview, edits, new Map([[0, ' Sales ']]))).toEqual({
      modules: [{ sheetIndex: 0, name: 'Sales' }],
      rows: [
        { id: '1-3', skip: true },
        { id: '1-4', title: 'Discount ignored', severity: 'High', assigneeId: 5 },
      ],
    });
    expect(buildCorrections(preview, new Map(), new Map())).toEqual({ modules: [], rows: [] });
  });

  it('asks to unassign when a matched assignee is removed', () => {
    const matched = row({ assigneeId: 4, status: 'Assigned' });
    const p = { ...preview, rows: [matched] };
    const edits = new Map([['1-3', { ...initialEdit(matched), assigneeId: null }]]);

    expect(buildCorrections(p, edits, new Map()).rows).toEqual([{ id: '1-3', unassign: true }]);
    expect(displayStatus(matched, edits.get('1-3')!)).toBe('New');
    expect(displayStatus(preview.rows[1], { ...initialEdit(preview.rows[1]), assigneeId: 5 })).toBe(
      'Assigned',
    );
  });

  it('clears the warnings an edit settles', () => {
    const r = preview.rows[1];
    expect(remainingWarnings(r, initialEdit(r))).toHaveLength(2);
    expect(remainingWarnings(r, { ...initialEdit(r), severity: 'Low' })).toEqual([r.warnings[1]]);
    expect(remainingWarnings(r, { ...initialEdit(r), severity: 'Low', assigneeId: 5 })).toEqual([]);
  });

  it('accepts only .xlsx workbooks up to 30 MB', () => {
    expect(workbookProblem(new File(['x'], 'Testing Sheet.XLSX'))).toBeNull();
    expect(workbookProblem(new File(['x'], 'sheet.xls'))).toContain('.xlsx');
    const big = new File(['x'], 'big.xlsx');
    Object.defineProperty(big, 'size', { value: 31 * 1024 * 1024 });
    expect(workbookProblem(big)).toContain('30 MB');
  });
});

describe('ImportPage', () => {
  beforeEach(() => {
    localStorage.setItem('qa.testing.5', JSON.stringify({ clientId: 1, projectId: 7 }));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeAuth(testUser('Tester', { id: 5 })),
      ],
    });
  });

  afterEach(() => localStorage.clear());

  function text(el: HTMLElement): string {
    return el.textContent!.replace(/\s+/g, ' ');
  }

  function button(el: HTMLElement, label: string): HTMLButtonElement {
    return [...el.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
      text(b).includes(label),
    )!;
  }

  it('walks from the workbook through review to the import summary', async () => {
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ImportPage);
    const el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();

    // 1 · drop a workbook
    expect(button(el, 'Next: where to').disabled).toBe(true);
    const drop = new Event('drop', { cancelable: true });
    Object.defineProperty(drop, 'dataTransfer', {
      value: { files: [new File(['xlsx'], 'Testing Sheet.xlsx')] },
    });
    el.querySelector('.qa-drop')!.dispatchEvent(drop);
    fixture.detectChanges();
    expect(text(el)).toContain('Testing Sheet.xlsx');
    button(el, 'Next: where to').click();
    fixture.detectChanges();

    // 2 · the remembered project's sprint in Testing is the target
    http
      .match((r) => r.url.includes('/lookups/cascade'))
      .forEach((r) =>
        r.flush({
          clients: [{ id: 1, name: 'Pardi Gears' }],
          projects: [{ id: 7, name: 'ERP', key: 'PGERP' }],
          modules: [],
          environments: [],
          members: [{ userId: 5, fullName: 'Emily Developer', role: 'Developer' }],
        }),
      );
    http.expectOne('/api/projects/7/sprints').flush([
      { id: 2, name: 'Sprint 0 – Early', status: 'Closed', startedAt: '2026-09-01T00:00:00Z' },
      { id: 3, name: 'Sprint 1 – UAT', status: 'Testing', startedAt: '2026-10-20T00:00:00Z' },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(text(el)).toContain('Into Sprint 1 – UAT (in Testing)');

    button(el, 'Read workbook').click();
    const read = http.expectOne('/api/import/excel/preview');
    const sent = read.request.body as FormData;
    expect(sent.get('sprintId')).toBe('3');
    expect((sent.get('file') as File).name).toBe('Testing Sheet.xlsx');
    read.flush(preview);
    fixture.detectChanges();
    await fixture.whenStable();

    // 3 · review: rows with warnings first, the summary counts
    expect(text(el)).toContain('50%');
    expect(el.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(text(el.querySelector('tbody')!)).toContain('matches no active developer');
    button(el, 'All (2)').click();
    fixture.detectChanges();
    expect(el.querySelectorAll('tbody tr')).toHaveLength(2);

    // skip the second row
    el.querySelectorAll<HTMLInputElement>('tbody input[type="checkbox"]')[1].click();
    fixture.detectChanges();
    expect(text(button(el, 'Import 1 issue')).trim()).toBe('Import 1 issue');

    button(el, 'Import 1 issue').click();
    const commit = http.expectOne('/api/import/excel/commit');
    expect(JSON.parse((commit.request.body as FormData).get('corrections') as string)).toEqual({
      modules: [],
      rows: [{ id: '1-4', skip: true }],
    });
    commit.flush({
      created: 1,
      skipped: 1,
      modulesCreated: 1,
      attachments: 2,
      firstKey: 'PGERP-143',
      lastKey: 'PGERP-143',
      warnings: [],
    });
    fixture.detectChanges();

    // 4 · summary with links
    expect(text(el)).toContain('1 issue imported into Sprint 1 – UAT');
    expect(text(el)).toContain('PGERP-143 · 2 screenshots · 1 new module · 1 row skipped');
    const open = [...el.querySelectorAll<HTMLAnchorElement>('a')].find((a) =>
      text(a).includes('Open the imported issues'),
    )!;
    expect(open.getAttribute('href')).toBe('/issues?client=1&project=7&sprint=3&sort=key');
  });

  it('shows why a workbook cannot be read and stays on the step', async () => {
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ImportPage);
    const el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();

    const drop = new Event('drop', { cancelable: true });
    Object.defineProperty(drop, 'dataTransfer', {
      value: { files: [new File(['x'], 'notes.csv')] },
    });
    el.querySelector('.qa-drop')!.dispatchEvent(drop);
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('.xlsx');
    expect(button(el, 'Next: where to').disabled).toBe(true);

    const ok = new Event('drop', { cancelable: true });
    Object.defineProperty(ok, 'dataTransfer', { value: { files: [new File(['x'], 'a.xlsx')] } });
    el.querySelector('.qa-drop')!.dispatchEvent(ok);
    fixture.detectChanges();
    button(el, 'Next: where to').click();
    fixture.detectChanges();
    http
      .match((r) => r.url.includes('/lookups/cascade'))
      .forEach((r) =>
        r.flush({ clients: [], projects: [], modules: [], environments: [], members: [] }),
      );
    http
      .expectOne('/api/projects/7/sprints')
      .flush([
        { id: 3, name: 'Sprint 1 – UAT', status: 'Testing', startedAt: '2026-10-20T00:00:00Z' },
      ]);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    button(el, 'Read workbook').click();
    http.expectOne('/api/import/excel/preview').flush(
      {
        title: 'Validation',
        detail: 'Choose the testing workbook as an Excel .xlsx file.',
        status: 400,
      },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')!.textContent).toContain(
      'Choose the testing workbook',
    );
    expect(button(el, 'Read workbook')).toBeTruthy();
  });
});
