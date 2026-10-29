import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL, SILENT_ERRORS } from '../../../core/api/api';

export interface OutboxMail {
  readonly id: number;
  readonly to: string;
  readonly subject: string;
  readonly attempts: number;
  readonly lastError: string | null;
  readonly createdAt: string;
  readonly nextAttemptAt: string | null;
  readonly sentAt: string | null;
}

/** Admin e-mail screen (ADR-029). The SMTP password is never sent – only whether one is set. */
export interface EmailStatus {
  readonly enabled: boolean;
  readonly workers: boolean;
  readonly from: string;
  readonly fromName: string;
  readonly appBaseUrl: string;
  readonly maxAttempts: number;
  readonly smtp: {
    readonly host: string;
    readonly port: number;
    readonly enableSsl: boolean;
    readonly userName: string | null;
    readonly passwordSet: boolean;
  };
  readonly outbox: {
    readonly pending: number;
    readonly failed: number;
    readonly sentLast24Hours: number;
    readonly oldestPendingAt: string | null;
    readonly lastSentAt: string | null;
  };
  readonly problems: readonly OutboxMail[];
  readonly recent: readonly OutboxMail[];
  readonly digest: {
    readonly timeIst: string;
    readonly lastRunAt: string | null;
    readonly lastResult: string | null;
  };
}

export interface TestEmailResult {
  readonly to: string;
  readonly durationMs: number;
  readonly server: string;
}

@Injectable({ providedIn: 'root' })
export class EmailAdminApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  status(): Observable<EmailStatus> {
    return this.http.get<EmailStatus>(`${this.baseUrl}/admin/email`, {
      context: new HttpContext().set(SILENT_ERRORS, true),
    });
  }

  /** The page shows the SMTP server's answer itself (no global toast). */
  sendTest(to: string): Observable<TestEmailResult> {
    return this.http.post<TestEmailResult>(
      `${this.baseUrl}/admin/email/test`,
      { to },
      { context: new HttpContext().set(SILENT_ERRORS, true) },
    );
  }

  runDigest(): Observable<{ queued: number }> {
    return this.http.post<{ queued: number }>(`${this.baseUrl}/admin/email/digest`, null);
  }

  retry(id: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/admin/email/outbox/${id}/retry`, null);
  }
}
