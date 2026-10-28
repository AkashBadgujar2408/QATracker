import { HttpClient, HttpContext, HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL, SILENT_ERRORS } from '../../core/api/api';

/** /api/issues/{key}/attachments (plan 14). URLs are signed and expire after about an hour – reload the list. */
export interface Attachment {
  readonly id: number;
  readonly issueId: number;
  readonly fileName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly isImage: boolean;
  readonly uploadedByName: string;
  readonly uploadedAt: string;
  readonly url: string;
  readonly thumbnailUrl: string | null;
}

/** Mirrors the API (FileInspector): images, PDF, XLSX, DOCX, TXT – max 10 MB each, 5 per upload. */
export const ATTACHMENT_RULES = {
  maxBytes: 10 * 1024 * 1024,
  maxFilesPerUpload: 5,
  extensions: ['.png', '.jpg', '.jpeg', '.gif', '.webp', '.pdf', '.xlsx', '.docx', '.txt', '.log'],
} as const;

/** Problem for a file the API would reject, or null. */
export function attachmentProblem(file: File): string | null {
  const dot = file.name.lastIndexOf('.');
  const extension = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';
  if (!(ATTACHMENT_RULES.extensions as readonly string[]).includes(extension)) {
    return `${file.name}: only images, PDF, Excel, Word and text files can be attached.`;
  }
  if (file.size === 0) {
    return `${file.name} is empty.`;
  }
  if (file.size > ATTACHMENT_RULES.maxBytes) {
    return `${file.name} is larger than 10 MB.`;
  }
  return null;
}

/** Upload errors are shown next to the file, not as a global toast. */
const inline = { context: new HttpContext().set(SILENT_ERRORS, true) };

@Injectable({ providedIn: 'root' })
export class AttachmentsApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  list(issueKey: string): Observable<Attachment[]> {
    return this.http.get<Attachment[]>(
      `${this.base}/issues/${encodeURIComponent(issueKey)}/attachments`,
    );
  }

  /** Upload with progress events (HttpEventType.UploadProgress, then Response). */
  upload(issueKey: string, files: readonly File[]): Observable<HttpEvent<Attachment[]>> {
    const form = new FormData();
    for (const file of files) {
      form.append('files', file, file.name);
    }
    return this.http.post<Attachment[]>(
      `${this.base}/issues/${encodeURIComponent(issueKey)}/attachments`,
      form,
      { ...inline, reportProgress: true, observe: 'events' },
    );
  }

  remove(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/attachments/${id}`);
  }
}
