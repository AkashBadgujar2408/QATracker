import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideFakeAuth, testUser } from '../../../testing/fake-auth';
import { EmailSettings } from '../../shared/data/notifications.api';
import { EmailAdminPage } from '../admin/email/email-admin-page';
import { EmailStatus } from '../admin/email/email-admin.api';
import { NotificationSettingsPage } from './notification-settings-page';

const settings: EmailSettings = {
  emailEnabled: true,
  digestTimeIst: '18:00',
  email: 'emily@example.com',
  items: [
    {
      type: 'Assigned',
      label: 'Issue assigned to you',
      description: 'd',
      delivery: 'Instant',
      email: true,
    },
    { type: 'Comment', label: 'Comments', description: 'd', delivery: 'Digest', email: true },
  ],
};

const status: EmailStatus = {
  enabled: true,
  workers: true,
  from: 'qa-tracker@example.com',
  fromName: 'QA Tracker',
  appBaseUrl: 'http://localhost:4200',
  maxAttempts: 8,
  smtp: {
    host: 'smtp.example.com',
    port: 587,
    enableSsl: true,
    userName: 'mailer',
    passwordSet: true,
  },
  outbox: { pending: 2, failed: 1, sentLast24Hours: 14, oldestPendingAt: null, lastSentAt: null },
  problems: [
    {
      id: 7,
      to: 'john@example.com',
      subject: 'PGERP-4 reopened',
      attempts: 8,
      lastError: 'SmtpException: Mailbox unavailable',
      createdAt: '2026-10-29T05:00:00Z',
      nextAttemptAt: null,
      sentAt: null,
    },
  ],
  recent: [],
  digest: { timeIst: '18:00', lastRunAt: null, lastResult: null },
};

function setup(role: 'Developer' | 'Admin' = 'Developer') {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      provideFakeAuth(testUser(role, { email: 'admin@example.com' })),
    ],
  });
  return TestBed.inject(HttpTestingController);
}

describe('NotificationSettingsPage', () => {
  it('groups instant and digest mails and saves one switch at a time', async () => {
    const http = setup();
    const fixture = TestBed.createComponent(NotificationSettingsPage);
    fixture.detectChanges();
    http.expectOne('/api/notifications/preferences').flush(settings);
    await fixture.whenStable();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Mailed right away');
    expect(text).toContain('every day at 18:00 IST');
    expect(text).toContain('emily@example.com');

    const toggle = fixture.nativeElement.querySelector(
      'mat-slide-toggle button',
    ) as HTMLButtonElement;
    toggle.click();
    const save = http.expectOne({ method: 'PUT', url: '/api/notifications/preferences' });
    expect(save.request.body).toEqual({ items: [{ type: 'Assigned', email: false }] });
    save.flush({ ...settings, items: [{ ...settings.items[0], email: false }, settings.items[1]] });
  });
});

describe('EmailAdminPage', () => {
  it('shows the settings without a password, sends a test mail and shows the SMTP answer', async () => {
    const http = setup('Admin');
    const fixture = TestBed.createComponent(EmailAdminPage);
    fixture.detectChanges();
    http.expectOne('/api/admin/email').flush(status);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('smtp.example.com:587 (TLS)');
    expect(el.textContent).toContain('password set');
    expect(el.textContent).toContain('Mailbox unavailable');

    const input = el.querySelector('input[type="email"]') as HTMLInputElement;
    expect(input.value).toBe('admin@example.com');
    (el.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    http.expectOne('/api/admin/email/test').flush(
      {
        title: 'Rule',
        status: 422,
        code: 'smtp_failed',
        detail:
          'The SMTP server smtp.example.com:587 did not accept the mail: 5.7.1 Relaying denied',
      },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await fixture.whenStable();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Relaying denied');

    (el.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    http
      .expectOne('/api/admin/email/test')
      .flush({ to: 'admin@example.com', durationMs: 84, server: 'smtp.example.com:587 (TLS)' });
    await fixture.whenStable();
    expect(el.querySelector('[role="status"].qa-result')?.textContent).toContain(
      'accepted the mail for admin@example.com in 84 ms',
    );
  });
});
