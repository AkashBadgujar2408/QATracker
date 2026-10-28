import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Attachment } from '../data/attachments.api';
import { formatFileSize } from '../pipes/file-size.pipe';
import { IssueAttachments } from './issue-attachments';

const screenshot: Attachment = {
  id: 7,
  issueId: 1,
  fileName: 'screenshot.png',
  contentType: 'image/png',
  sizeBytes: 48_000,
  isImage: true,
  uploadedByName: 'Test Tester',
  uploadedAt: '2026-10-28T04:30:00Z',
  url: '/api/attachments/7?u=1&exp=1&sig=a',
  thumbnailUrl: '/api/attachments/7/thumbnail?u=1&exp=1&sig=b',
};

const spec: Attachment = {
  ...screenshot,
  id: 8,
  fileName: 'spec.pdf',
  contentType: 'application/pdf',
  isImage: false,
  url: '/api/attachments/8?u=1&exp=1&sig=c',
  thumbnailUrl: null,
};

describe('IssueAttachments', () => {
  let http: HttpTestingController;

  async function render(canEdit: boolean) {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(IssueAttachments);
    fixture.componentRef.setInput('issueKey', 'PGERP-1');
    fixture.componentRef.setInput('canEdit', canEdit);
    TestBed.tick();
    return fixture;
  }

  it('shows image thumbnails and download links for other files', async () => {
    const fixture = await render(false);
    http.expectOne('/api/issues/PGERP-1/attachments').flush([screenshot, spec]);
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('img')!.getAttribute('src')).toBe(screenshot.thumbnailUrl);
    expect(host.querySelector('a[aria-label="Download spec.pdf"]')!.getAttribute('href')).toBe(
      spec.url,
    );
    expect(host.textContent).toContain('28/Oct/2026');
    expect(host.querySelector('app-paste-dropzone')).toBeNull();
    expect(host.querySelector('button[aria-label^="Remove"]')).toBeNull();
  });

  it('uploads added files at once and reloads the list', async () => {
    const fixture = await render(true);
    http.expectOne('/api/issues/PGERP-1/attachments').flush([]);
    await fixture.whenStable();

    const dropzone = fixture.debugElement.query((el) => el.name === 'app-paste-dropzone');
    dropzone.componentInstance.addFiles([
      new File([new Uint8Array(4)], 'bug.png', { type: 'image/png' }),
    ]);
    await fixture.whenStable();

    const upload = http.expectOne(
      (r) => r.method === 'POST' && r.url === '/api/issues/PGERP-1/attachments',
    );
    expect((upload.request.body as FormData).getAll('files')).toHaveLength(1);
    upload.flush([screenshot], { status: 201, statusText: 'Created' });
    TestBed.tick();
    http.expectOne('/api/issues/PGERP-1/attachments').flush([screenshot]);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('screenshot.png');
  });

  it('shows the API reason when an upload is refused', async () => {
    const fixture = await render(true);
    http.expectOne('/api/issues/PGERP-1/attachments').flush([]);
    await fixture.whenStable();

    const dropzone = fixture.debugElement.query((el) => el.name === 'app-paste-dropzone');
    dropzone.componentInstance.addFiles([
      new File([new Uint8Array(4)], 'fake.png', { type: 'image/png' }),
    ]);
    http
      .expectOne((r) => r.method === 'POST')
      .flush(
        { errors: { files: ['fake.png does not look like a PNG file.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'does not look like a PNG',
    );
  });

  it('formats file sizes', () => {
    expect([formatFileSize(812), formatFileSize(48_000), formatFileSize(2_516_582)]).toEqual([
      '812 B',
      '47 KB',
      '2.4 MB',
    ]);
  });
});
