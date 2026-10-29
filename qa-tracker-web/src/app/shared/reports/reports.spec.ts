import { HttpClient, HttpParams, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ToastService } from '../../core/feedback/toast.service';
import { IssuesApi } from '../data/issues.api';
import { fileNameFrom, getFile, saveFile } from '../data/download';
import { SprintReportButton } from './sprint-report-button';

describe('file downloads', () => {
  it('reads the file name from Content-Disposition, preferring filename*', () => {
    expect(
      fileNameFrom(
        `attachment; filename="PGERP Sprint 2 - UAT report.xlsx"; filename*=UTF-8''PGERP%20Sprint%202%20%E2%80%93%20UAT%20report.xlsx`,
        'x',
      ),
    ).toBe('PGERP Sprint 2 – UAT report.xlsx');
    expect(fileNameFrom('attachment; filename=Issues.xlsx', 'x')).toBe('Issues.xlsx');
    expect(fileNameFrom(null, 'Sprint report.pdf')).toBe('Sprint report.pdf');
  });

  it('saves through a temporary link with the server name', () => {
    const create = vi.fn(() => 'blob:report');
    const revoke = vi.fn();
    Object.assign(URL, { createObjectURL: create, revokeObjectURL: revoke });
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => undefined);

    saveFile({ blob: new Blob(['x']), fileName: 'Issues 29-Oct-2026.xlsx' });

    expect(create).toHaveBeenCalled();
    const link = click.mock.contexts[0] as HTMLAnchorElement;
    expect(link.download).toBe('Issues 29-Oct-2026.xlsx');
    expect(document.body.contains(link)).toBe(false);
    click.mockRestore();
  });

  it('exports the issue list with repeated filters and no paging', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    let name = '';

    TestBed.inject(IssuesApi)
      .export({ projectId: 7, status: ['New', 'Fixed'], sort: 'key' })
      .subscribe((file) => (name = file.fileName));

    const request = http.expectOne((r) => r.url === '/api/issues/export');
    expect(request.request.responseType).toBe('blob');
    expect(request.request.params.getAll('status')).toEqual(['New', 'Fixed']);
    expect(request.request.params.get('projectId')).toBe('7');
    request.flush(new Blob(['x']), {
      headers: { 'Content-Disposition': 'attachment; filename="Issues 29-Oct-2026.xlsx"' },
    });
    expect(name).toBe('Issues 29-Oct-2026.xlsx');
  });

  it('uses the fallback name when the header is not readable', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    let name = '';
    getFile(TestBed.inject(HttpClient), '/api/x', new HttpParams(), 'Sprint report.pdf').subscribe(
      (file) => (name = file.fileName),
    );
    http.expectOne('/api/x').flush(new Blob(['x']));
    expect(name).toBe('Sprint report.pdf');
  });
});

describe('SprintReportButton', () => {
  function setup() {
    const error = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: { error, success: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(SprintReportButton);
    fixture.componentRef.setInput('sprintId', 3);
    fixture.detectChanges();
    return { fixture, http: TestBed.inject(HttpTestingController), error };
  }

  function choose(fixture: ReturnType<typeof setup>['fixture'], label: string): void {
    (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    const item = [...document.querySelectorAll<HTMLButtonElement>('[mat-menu-item]')].find((b) =>
      b.textContent!.includes(label),
    )!;
    item.click();
    fixture.detectChanges();
  }

  it('downloads the chosen format and shows progress meanwhile', () => {
    const { fixture, http } = setup();
    Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:r'), revokeObjectURL: vi.fn() });
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => undefined);

    choose(fixture, 'PDF');
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(button.textContent).toContain('Preparing PDF…');
    expect(button.disabled).toBe(true);

    http.expectOne('/api/sprints/3/report?format=pdf').flush(new Blob(['%PDF']));
    fixture.detectChanges();
    expect(click).toHaveBeenCalled();
    expect(button.textContent).toContain('Download report');
    click.mockRestore();
  });

  it('tells the user when the report fails', () => {
    const { fixture, http, error } = setup();
    choose(fixture, 'Excel');
    http
      .expectOne('/api/sprints/3/report?format=xlsx')
      .flush(new Blob(), { status: 500, statusText: 'Server error' });
    expect(error).toHaveBeenCalledWith('The report could not be created. Try again in a moment.');
  });
});
