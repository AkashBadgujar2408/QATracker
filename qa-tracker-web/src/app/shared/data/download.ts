import { HttpClient, HttpContext, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { SILENT_ERRORS } from '../../core/api/api';

/** A file from the API: its bytes and the name the server gave it. */
export interface DownloadedFile {
  readonly blob: Blob;
  readonly fileName: string;
}

/** The file name of a Content-Disposition header (RFC 6266 `filename*` first, then `filename`). */
export function fileNameFrom(disposition: string | null, fallback: string): string {
  if (!disposition) {
    return fallback;
  }

  const star = /filename\*\s*=\s*(?:UTF-8|utf-8)''([^;]+)/.exec(disposition);
  if (star) {
    try {
      return decodeURIComponent(star[1].trim());
    } catch {
      // fall through to the plain name
    }
  }

  const plain = /filename\s*=\s*"?([^";]+)"?/.exec(disposition);
  return plain ? plain[1].trim() : fallback;
}

/**
 * GETs a file with the access token (a plain link would not send it). Errors are left to the caller – the body of a
 * failed blob request is not a readable problem.
 */
export function getFile(
  http: HttpClient,
  url: string,
  params: HttpParams,
  fallbackName: string,
): Observable<DownloadedFile> {
  return http
    .get(url, {
      params,
      observe: 'response',
      responseType: 'blob',
      context: new HttpContext().set(SILENT_ERRORS, true),
    })
    .pipe(
      map((response: HttpResponse<Blob>) => ({
        blob: response.body ?? new Blob(),
        fileName: fileNameFrom(response.headers.get('Content-Disposition'), fallbackName),
      })),
    );
}

/** Hands the file to the browser's download. */
export function saveFile(file: DownloadedFile, doc: Document = document): void {
  const url = URL.createObjectURL(file.blob);
  const link = doc.createElement('a');
  link.href = url;
  link.download = file.fileName;
  link.rel = 'noopener';
  doc.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
