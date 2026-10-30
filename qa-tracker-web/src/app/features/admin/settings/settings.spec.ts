import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { provideFakeAuth, testUser } from '../../../../testing/fake-auth';
import { overdueLabel } from '../../../shared/ui/overdue-badge/overdue-badge';
import { MailServerSettings } from '../email/email-admin.api';
import { MailServerDialog } from '../email/mail-server-dialog';
import { HolidaySettings } from './holiday-settings';
import { PhaseSettings } from './phase-settings';
import { Phase, SlaPolicy, describeWorkingHours, isoDate } from './settings.api';
import { SlaSettings } from './sla-settings';

const SLA: SlaPolicy[] = [
  { severity: 'Critical', workingHours: 56, rowVersion: 'AAAAAAAAAAE=' },
  { severity: 'High', workingHours: 112, rowVersion: 'AAAAAAAAAAI=' },
  { severity: 'Medium', workingHours: 168, rowVersion: 'AAAAAAAAAAM=' },
  { severity: 'Low', workingHours: 224, rowVersion: 'AAAAAAAAAAQ=' },
  { severity: 'NotAssessed', workingHours: 280, rowVersion: 'AAAAAAAAAAU=' },
];

const phase = (id: number, name: string, sortOrder: number): Phase => ({
  id,
  name,
  sortOrder,
  isActive: true,
  sprintCount: id,
  rowVersion: `AAAAAAAAAA${id}=`,
});

describe('admin settings', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeAuth(testUser('Admin')),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  const text = (el: HTMLElement) => (el.textContent ?? '').replace(/\s+/g, ' ');
  const button = (el: HTMLElement, label: string) =>
    [...el.querySelectorAll<HTMLButtonElement>('button')].find((b) => text(b).includes(label))!;

  it('describes working hours as working days', () => {
    expect(describeWorkingHours(56)).toBe('7 working days');
    expect(describeWorkingHours(8)).toBe('1 working day');
    expect(describeWorkingHours(12)).toBe('1 working day 4 h');
    expect(describeWorkingHours(3)).toBe('3 h');
    expect(isoDate(new Date(2026, 0, 26))).toBe('2026-01-26');
  });

  it('labels how long an issue is overdue', () => {
    const now = new Date('2026-10-30T12:00:00Z');
    expect(overdueLabel('2026-10-30T09:00:00Z', now)).toBe('Overdue');
    expect(overdueLabel('2026-10-27T11:00:00Z', now)).toBe('Overdue 3 d');
  });

  it('SLA: draws each window to one scale and saves only the changed severity', async () => {
    const fixture = TestBed.createComponent(SlaSettings);
    TestBed.tick();
    http.expectOne('/api/settings/sla').flush(SLA);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(text(el)).toContain('7 working days');
    const bars = [...el.querySelectorAll<HTMLElement>('.qa-sla-bar')].map((b) => b.style.width);
    expect(bars).toEqual(['20%', '40%', '60%', '80%', '100%']);
    expect(button(el, 'Save SLA').disabled).toBe(true);

    const critical = el.querySelector<HTMLInputElement>(
      'input[aria-label="Working hours for Critical"]',
    )!;
    critical.value = '0';
    critical.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(text(el)).toContain('1 to 2400 hours');
    expect(button(el, 'Save SLA').disabled).toBe(true);

    critical.value = '28';
    critical.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(text(el)).toContain('3 working days 4 h');
    button(el, 'Save SLA').click();

    const save = http.expectOne('/api/settings/sla');
    expect(save.request.method).toBe('PUT');
    expect(save.request.body).toEqual({
      policies: [{ severity: 'Critical', workingHours: 28, rowVersion: 'AAAAAAAAAAE=' }],
    });
    save.flush(SLA.map((p) => (p.severity === 'Critical' ? { ...p, workingHours: 28 } : p)));
    await fixture.whenStable();
    expect(button(el, 'Save SLA').disabled).toBe(true);
  });

  it('holidays: groups by year, newest first, with the weekday', async () => {
    const fixture = TestBed.createComponent(HolidaySettings);
    TestBed.tick();
    http.expectOne('/api/settings/holidays').flush([
      { date: '2026-11-08', name: 'Diwali' },
      { date: '2027-01-26', name: 'Republic Day' },
    ]);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    const headings = [...el.querySelectorAll('h3')].map((h) => text(h).trim());
    expect(headings).toEqual(['2027 · 1', '2026 · 1']);
    expect(text(el)).toContain('26/Jan/2027');
    expect(text(el)).toContain('Tue');
    expect(text(el)).toContain('08/Nov/2026');
  });

  it('phases: moving a phase sends the new order with every version', async () => {
    const fixture = TestBed.createComponent(PhaseSettings);
    TestBed.tick();
    const phases = [phase(1, 'Early Testing', 1), phase(2, 'UAT', 2), phase(3, 'Live', 3)];
    http.expectOne('/api/settings/phases').flush(phases);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(text(el)).toContain('2 sprints');
    el.querySelector<HTMLButtonElement>('button[aria-label="Move Live up"]')!.click();
    await fixture.whenStable();

    const order = http.expectOne('/api/settings/phases/order');
    expect(order.request.body).toEqual({
      phases: [
        { id: 1, rowVersion: 'AAAAAAAAAA1=' },
        { id: 3, rowVersion: 'AAAAAAAAAA3=' },
        { id: 2, rowVersion: 'AAAAAAAAAA2=' },
      ],
    });
    order.flush([phases[0], { ...phases[2], sortOrder: 2 }, { ...phases[1], sortOrder: 3 }]);
    await fixture.whenStable();
    const names = [...el.querySelectorAll<HTMLInputElement>('input.qa-name')].map((i) => i.value);
    expect(names).toEqual(['Early Testing', 'Live', 'UAT']);
  });

  it('mail server dialog keeps the saved password unless one is typed or removal is ticked', async () => {
    const settings: MailServerSettings = {
      smtpHost: 'smtp.office.local',
      smtpPort: 587,
      smtpEnableSsl: true,
      smtpUserName: 'relay',
      passwordSet: true,
      fromAddress: 'qa@office.local',
      fromName: 'QA Tracker',
      savedInApp: true,
      updatedAt: '2026-10-30T10:00:00Z',
      updatedBy: 'Emily Admin',
      rowVersion: 'AAAAAAAAAAk=',
    };
    const close = vi.fn();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: settings },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(MailServerDialog);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(text(el)).toContain('A password is saved.');
    button(el, 'Save mail server').click();
    const keep = http.expectOne('/api/admin/email/settings');
    expect(keep.request.body).toMatchObject({
      smtpUserName: 'relay',
      smtpPassword: null,
      rowVersion: 'AAAAAAAAAAk=',
    });
    keep.flush(settings);
    await fixture.whenStable();
    expect(close).toHaveBeenCalledWith(settings);

    const again = TestBed.createComponent(MailServerDialog);
    await again.whenStable();
    const second = again.nativeElement as HTMLElement;
    second.querySelector<HTMLInputElement>('mat-checkbox input')!.click();
    await again.whenStable();
    button(second, 'Save mail server').click();
    expect(http.expectOne('/api/admin/email/settings').request.body).toMatchObject({
      smtpPassword: '',
    });
  });
});
