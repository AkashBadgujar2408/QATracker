import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleCheck,
  lucideCircleX,
  lucideMailCheck,
  lucidePencil,
  lucideRefreshCw,
  lucideSend,
} from '@ng-icons/lucide';
import { problemMessage } from '../../../core/api/api';
import { AuthService } from '../../../core/auth/auth.service';
import { ToastService } from '../../../core/feedback/toast.service';
import { IstDatePipe, relativeTime } from '../../../shared/pipes/ist-date.pipe';
import { EmptyState } from '../../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../../shared/ui/page-header/page-header';
import { SkeletonLines } from '../../../shared/ui/skeleton/skeleton';
import { MatDialog } from '@angular/material/dialog';
import { ConfirmService } from '../../../core/feedback/confirm.service';
import { EmailAdminApi, MailServerSettings, OutboxMail, TestEmailResult } from './email-admin.api';
import { MailServerDialog } from './mail-server-dialog';

type TestState =
  | { readonly kind: 'idle' }
  | { readonly kind: 'sending' }
  | { readonly kind: 'sent'; readonly result: TestEmailResult }
  | { readonly kind: 'failed'; readonly message: string };

/**
 * Admin › E-mail (ADR-029): the SMTP settings the server runs with, a test mail that shows the server's answer, the
 * outbox (queued, failed, sent) with retry, and the daily summary. The mail server can be edited here (saved in the
 * app, ADR-038); until then the configuration applies.
 */
@Component({
  selector: 'app-email-admin-page',
  imports: [
    PageHeader,
    EmptyState,
    SkeletonLines,
    MatButton,
    MatFormField,
    MatLabel,
    MatHint,
    MatInput,
    FormsModule,
    NgIcon,
    IstDatePipe,
  ],
  viewProviders: [
    provideIcons({
      lucideCircleCheck,
      lucideCircleX,
      lucideMailCheck,
      lucidePencil,
      lucideRefreshCw,
      lucideSend,
    }),
  ],
  template: `
    <app-page-header
      title="E-mail"
      subtitle="Check the mail settings, send a test mail and see what is waiting to go out."
    >
      <button actions matButton="outlined" type="button" (click)="status.reload()">
        <ng-icon name="lucideRefreshCw" size="16" class="mr-1" />
        Refresh
      </button>
    </app-page-header>

    @if (status.isLoading() && !status.hasValue()) {
      <div class="grid gap-4 lg:grid-cols-2">
        <div class="rounded-card border border-border bg-surface p-5">
          <app-skeleton-lines [lines]="6" />
        </div>
        <div class="rounded-card border border-border bg-surface p-5">
          <app-skeleton-lines [lines]="6" />
        </div>
      </div>
    } @else if (status.error() && !status.hasValue()) {
      <app-empty-state
        kind="error"
        title="E-mail status could not be loaded"
        [message]="loadError()"
      >
        <button matButton="outlined" type="button" (click)="status.reload()">Try again</button>
      </app-empty-state>
    } @else if (status.hasValue()) {
      @let s = status.value();
      <div
        class="mb-4 flex items-center gap-3 rounded-card border border-border bg-surface px-4 py-3"
        role="status"
      >
        <span class="qa-dot" [class.qa-dot-on]="s.enabled" aria-hidden="true"></span>
        <p class="m-0 text-base text-text">
          @if (s.enabled) {
            E-mail is <strong>on</strong>. Notifications are mailed through
            <span class="font-medium">{{ server() }}</span>
            @if (!s.workers) {
              – but the sender does not run in this process (Email:Workers is off).
            }
          } @else {
            E-mail is <strong>off</strong> (Email:Enabled). Notifications stay in the app; you can
            still send a test mail to check the settings before switching it on.
          }
        </p>
      </div>

      <div class="grid gap-4 lg:grid-cols-[3fr_2fr]">
        <section
          class="rounded-card border border-border bg-surface p-5"
          aria-labelledby="test-mail"
        >
          <h2 id="test-mail" class="m-0 text-md font-semibold text-text">Send a test mail</h2>
          <p class="m-0 mt-1 mb-4 text-sm text-muted">
            Goes straight to the SMTP server, not through the queue, so you see its answer here.
          </p>
          <form class="flex flex-wrap items-start gap-3" (ngSubmit)="sendTest()">
            <mat-form-field class="min-w-64 flex-1" subscriptSizing="dynamic">
              <mat-label>Send to</mat-label>
              <input
                matInput
                type="email"
                name="to"
                autocomplete="email"
                [ngModel]="testTo()"
                (ngModelChange)="testTo.set($event)"
                required
              />
              <mat-hint>Your own address is filled in.</mat-hint>
            </mat-form-field>
            <button
              matButton="filled"
              type="submit"
              class="mt-2"
              [disabled]="test().kind === 'sending' || !testTo().trim()"
            >
              <ng-icon name="lucideSend" size="16" class="mr-1" />
              {{ test().kind === 'sending' ? 'Sending…' : 'Send test mail' }}
            </button>
          </form>
          @switch (test().kind) {
            @case ('sent') {
              @let r = sentResult()!;
              <p class="qa-result qa-result-ok m-0 mt-4" role="status">
                <ng-icon name="lucideCircleCheck" size="18" />
                <span>
                  {{ r.server }} accepted the mail for <strong>{{ r.to }}</strong> in
                  {{ r.durationMs }} ms. Check that inbox.
                </span>
              </p>
            }
            @case ('failed') {
              <p class="qa-result qa-result-bad m-0 mt-4" role="alert">
                <ng-icon name="lucideCircleX" size="18" />
                <span>{{ failedMessage() }}</span>
              </p>
            }
          }

          <h3 class="m-0 mt-6 mb-2 text-sm font-semibold text-muted">Settings in use</h3>
          <dl class="qa-dl m-0">
            <dt>SMTP server</dt>
            <dd>{{ s.smtp.host }}:{{ s.smtp.port }}</dd>
            <dt>Encryption</dt>
            <dd>{{ s.smtp.enableSsl ? 'TLS' : 'None' }}</dd>
            <dt>Sign-in</dt>
            <dd>
              @if (s.smtp.userName) {
                {{ s.smtp.userName }} · password {{ s.smtp.passwordSet ? 'set' : 'not set' }}
              } @else {
                None (anonymous relay)
              }
            </dd>
            <dt>Sender</dt>
            <dd>{{ s.fromName }} &lt;{{ s.from }}&gt;</dd>
            <dt>Links point to</dt>
            <dd>{{ s.appBaseUrl }}</dd>
            <dt>Retries</dt>
            <dd>up to {{ s.maxAttempts }} attempts, waiting 1, 5, 15, 30 min, then hourly</dd>
          </dl>
          <div class="mt-4 flex flex-wrap items-center gap-3">
            <button
              matButton="outlined"
              type="button"
              [disabled]="!mailServer.hasValue()"
              (click)="editMailServer()"
            >
              <ng-icon name="lucidePencil" size="16" class="mr-1" />
              Edit mail server
            </button>
            @if (mailServer.value()?.savedInApp) {
              <button matButton type="button" (click)="resetMailServer()">
                Use the configuration
              </button>
            }
          </div>
          @if (mailServer.value(); as m) {
            <p class="m-0 mt-2 text-sm text-muted">
              @if (m.savedInApp) {
                Saved here by {{ m.updatedBy }} on {{ m.updatedAt | istDate: 'datetime' }}.
              } @else {
                From the server configuration (appsettings, section Email) until you save them here.
              }
            </p>
          }
        </section>

        <div class="flex flex-col gap-4">
          <section
            class="rounded-card border border-border bg-surface p-5"
            aria-labelledby="outbox"
          >
            <h2 id="outbox" class="m-0 mb-3 text-md font-semibold text-text">Outbox</h2>
            <div class="grid grid-cols-3 gap-3">
              <div class="qa-stat">
                <span class="qa-stat-value">{{ s.outbox.pending }}</span>
                <span class="qa-stat-label">waiting</span>
              </div>
              <div class="qa-stat" [class.qa-stat-bad]="s.outbox.failed > 0">
                <span class="qa-stat-value">{{ s.outbox.failed }}</span>
                <span class="qa-stat-label">gave up</span>
              </div>
              <div class="qa-stat">
                <span class="qa-stat-value">{{ s.outbox.sentLast24Hours }}</span>
                <span class="qa-stat-label">sent in 24 h</span>
              </div>
            </div>
            <p class="m-0 mt-3 text-sm text-muted">
              @if (s.outbox.oldestPendingAt) {
                Oldest waiting since {{ ago(s.outbox.oldestPendingAt) }}.
              }
              @if (s.outbox.lastSentAt) {
                Last sent {{ ago(s.outbox.lastSentAt) }}.
              } @else {
                Nothing sent yet.
              }
            </p>
          </section>

          <section
            class="rounded-card border border-border bg-surface p-5"
            aria-labelledby="digest"
          >
            <h2 id="digest" class="m-0 text-md font-semibold text-text">Daily summary</h2>
            <p class="m-0 mt-1 text-sm text-muted">
              Comments and overdue issues, mailed every day at {{ s.digest.timeIst }} IST.
            </p>
            <p class="m-0 mt-3 text-sm text-text">
              @if (s.digest.lastRunAt) {
                Last run {{ s.digest.lastRunAt | istDate: 'datetime' }}
                @if (s.digest.lastResult) {
                  <span class="text-muted">– {{ s.digest.lastResult }}</span>
                }
              } @else {
                Not run yet.
              }
            </p>
            <button
              matButton="outlined"
              type="button"
              class="mt-3"
              [disabled]="!s.enabled || digestBusy()"
              (click)="runDigest()"
            >
              <ng-icon name="lucideMailCheck" size="16" class="mr-1" />
              {{ digestBusy() ? 'Queueing…' : 'Send the summary now' }}
            </button>
          </section>
        </div>
      </div>

      @if (s.problems.length > 0) {
        <section
          class="mt-4 rounded-card border border-border bg-surface"
          aria-labelledby="problems"
        >
          <h2 id="problems" class="m-0 px-5 pt-4 pb-2 text-md font-semibold text-text">
            Not delivered
          </h2>
          <ul class="qa-list m-0 list-none p-0">
            @for (m of s.problems; track m.id) {
              <li class="flex flex-wrap items-start gap-3 px-5 py-3">
                <div class="min-w-0 flex-1">
                  <p class="m-0 text-sm font-medium text-text">{{ m.subject }}</p>
                  <p class="m-0 mt-0.5 text-sm text-muted">
                    To {{ m.to }} · {{ m.attempts }} of {{ s.maxAttempts }} attempts
                    @if (m.nextAttemptAt && m.attempts < s.maxAttempts) {
                      · next {{ m.nextAttemptAt | istDate: 'datetime' }}
                    }
                  </p>
                  <p class="m-0 mt-1 text-sm text-severity-critical">{{ m.lastError }}</p>
                </div>
                <button
                  matButton="outlined"
                  type="button"
                  [disabled]="retrying() === m.id"
                  (click)="retry(m)"
                >
                  Try again
                </button>
              </li>
            }
          </ul>
        </section>
      }

      <section class="mt-4 rounded-card border border-border bg-surface" aria-labelledby="recent">
        <h2 id="recent" class="m-0 px-5 pt-4 pb-2 text-md font-semibold text-text">Latest mails</h2>
        @if (s.recent.length === 0) {
          <p class="m-0 px-5 pb-4 text-sm text-muted">No mails yet.</p>
        } @else {
          <ul class="qa-list m-0 list-none p-0">
            @for (m of s.recent; track m.id) {
              <li class="flex flex-wrap items-center gap-x-4 gap-y-1 px-5 py-2.5 text-sm">
                <span class="min-w-0 flex-1 truncate text-text" [title]="m.subject">{{
                  m.subject
                }}</span>
                <span class="text-muted">{{ m.to }}</span>
                <span class="qa-state" [attr.data-state]="state(m, s.maxAttempts)">{{
                  stateLabel(m, s.maxAttempts)
                }}</span>
                <span class="w-36 text-right text-muted tabular-nums">{{
                  m.sentAt ?? m.createdAt | istDate: 'datetime'
                }}</span>
              </li>
            }
          </ul>
        }
      </section>
    }
  `,
  styles: `
    .qa-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      flex-shrink: 0;
      background: var(--qa-text-muted);
    }
    .qa-dot-on {
      background: var(--qa-status-verified);
      box-shadow: 0 0 0 4px color-mix(in srgb, var(--qa-status-verified) 20%, transparent);
    }
    .qa-result {
      display: flex;
      gap: 8px;
      align-items: flex-start;
      padding: 10px 12px;
      border-radius: var(--qa-radius-control);
      font-size: var(--qa-text-sm);
      line-height: 1.45;
    }
    .qa-result ng-icon {
      flex-shrink: 0;
      margin-top: 1px;
    }
    .qa-result-ok {
      color: var(--qa-text);
      background: color-mix(in srgb, var(--qa-status-verified) 12%, transparent);
    }
    .qa-result-ok ng-icon {
      color: var(--qa-status-verified);
    }
    .qa-result-bad {
      color: var(--qa-text);
      background: color-mix(in srgb, var(--qa-severity-critical) 10%, transparent);
    }
    .qa-result-bad ng-icon {
      color: var(--qa-severity-critical);
    }
    .qa-dl {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 6px 16px;
      font-size: var(--qa-text-sm);
    }
    .qa-dl dt {
      color: var(--qa-text-muted);
    }
    .qa-dl dd {
      margin: 0;
      color: var(--qa-text);
      overflow-wrap: anywhere;
    }
    .qa-stat {
      display: flex;
      flex-direction: column;
      padding: 10px 12px;
      border-radius: var(--qa-radius-control);
      background: var(--qa-surface-muted);
    }
    .qa-stat-value {
      font-size: var(--qa-text-xl);
      font-weight: 600;
      line-height: 1.1;
      color: var(--qa-text);
      font-variant-numeric: tabular-nums;
    }
    .qa-stat-label {
      font-size: var(--qa-text-xs);
      color: var(--qa-text-muted);
    }
    .qa-stat-bad .qa-stat-value {
      color: var(--qa-severity-critical);
    }
    .qa-list > li + li {
      border-top: 1px solid var(--qa-border);
    }
    .qa-state {
      font-size: var(--qa-text-xs);
      font-weight: 600;
      padding: 1px 8px;
      border-radius: var(--qa-radius-pill);
      background: var(--qa-surface-muted);
      color: var(--qa-text-muted);
    }
    .qa-state[data-state='sent'] {
      color: var(--qa-status-closed);
      background: color-mix(in srgb, var(--qa-status-verified) 14%, transparent);
    }
    .qa-state[data-state='failed'] {
      color: var(--qa-severity-critical);
      background: color-mix(in srgb, var(--qa-severity-critical) 12%, transparent);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmailAdminPage {
  private readonly api = inject(EmailAdminApi);
  private readonly toast = inject(ToastService);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(ConfirmService);

  protected readonly status = rxResource({ stream: () => this.api.status() });
  protected readonly mailServer = rxResource({ stream: () => this.api.mailServer() });
  protected readonly loadError = computed(() =>
    problemMessage(this.status.error(), 'E-mail status could not be loaded.'),
  );
  protected readonly server = computed(() => {
    const s = this.status.value()?.smtp;
    return s ? `${s.host}:${s.port}${s.enableSsl ? ' (TLS)' : ''}` : '';
  });

  protected readonly testTo = signal(inject(AuthService).user()?.email ?? '');
  protected readonly test = signal<TestState>({ kind: 'idle' });
  protected readonly sentResult = computed(() => {
    const t = this.test();
    return t.kind === 'sent' ? t.result : null;
  });
  protected readonly failedMessage = computed(() => {
    const t = this.test();
    return t.kind === 'failed' ? t.message : '';
  });
  protected readonly digestBusy = signal(false);
  protected readonly retrying = signal<number | null>(null);
  private readonly now = new Date();

  protected sendTest(): void {
    const to = this.testTo().trim();
    if (!to || this.test().kind === 'sending') {
      return;
    }
    this.test.set({ kind: 'sending' });
    this.api.sendTest(to).subscribe({
      next: (result) => this.test.set({ kind: 'sent', result }),
      error: (error: unknown) =>
        this.test.set({ kind: 'failed', message: problemMessage(error, 'The test mail failed.') }),
    });
  }

  protected editMailServer(): void {
    const settings = this.mailServer.value();
    if (!settings) {
      return;
    }
    this.dialog
      .open<MailServerDialog, MailServerSettings, MailServerSettings>(MailServerDialog, {
        data: settings,
        width: '560px',
        maxWidth: 'calc(100vw - 32px)',
        restoreFocus: true,
      })
      .afterClosed()
      .subscribe((saved) => {
        if (saved) {
          this.mailServerChanged(saved, 'Mail server saved. The next mail uses it.');
        }
      });
  }

  protected async resetMailServer(): Promise<void> {
    const ok = await this.confirm.ask({
      title: 'Use the configuration again?',
      message:
        'The mail server saved here is forgotten; mails go out with the settings in appsettings (section Email).',
      confirmLabel: 'Use the configuration',
    });
    if (ok) {
      this.api
        .resetMailServer()
        .subscribe((settings) =>
          this.mailServerChanged(settings, 'The configured mail server is used again.'),
        );
    }
  }

  private mailServerChanged(settings: MailServerSettings, message: string): void {
    this.mailServer.set(settings);
    this.test.set({ kind: 'idle' });
    this.status.reload();
    this.toast.success(message);
  }

  protected runDigest(): void {
    this.digestBusy.set(true);
    this.api.runDigest().subscribe({
      next: ({ queued }) => {
        this.digestBusy.set(false);
        this.toast.success(
          queued === 0
            ? 'Nothing to summarise right now.'
            : `Summary queued for ${queued} ${queued === 1 ? 'person' : 'people'}.`,
        );
        this.status.reload();
      },
      error: () => this.digestBusy.set(false),
    });
  }

  protected retry(mail: OutboxMail): void {
    this.retrying.set(mail.id);
    this.api.retry(mail.id).subscribe({
      next: () => {
        this.retrying.set(null);
        this.toast.success('Queued again – the sender tries it within a few seconds.');
        this.status.reload();
      },
      error: () => this.retrying.set(null),
    });
  }

  protected ago(iso: string): string {
    return relativeTime(iso, this.now);
  }

  protected state(m: OutboxMail, max: number): 'sent' | 'failed' | 'waiting' {
    return m.sentAt ? 'sent' : m.attempts >= max ? 'failed' : 'waiting';
  }

  protected stateLabel(m: OutboxMail, max: number): string {
    const state = this.state(m, max);
    return state === 'sent'
      ? 'Sent'
      : state === 'failed'
        ? 'Gave up'
        : m.attempts > 0
          ? `Retrying (${m.attempts})`
          : 'Waiting';
  }
}
