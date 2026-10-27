import { TestBed } from '@angular/core/testing';
import { ISSUE_STATUSES } from '../../models/issue-enums';
import { SeverityBadge } from '../severity-badge/severity-badge';
import { StatusPill } from './status-pill';

describe('StatusPill', () => {
  it.each(ISSUE_STATUSES)('renders %s with a label and its colour token', async (status) => {
    const fixture = TestBed.createComponent(StatusPill);
    fixture.componentRef.setInput('status', status);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent?.trim().length).toBeGreaterThan(0);
    expect(host.style.getPropertyValue('--pill-color')).toMatch(/^var\(--qa-status-/);
  });

  it('shows "In Progress" for InProgress', async () => {
    const fixture = TestBed.createComponent(StatusPill);
    fixture.componentRef.setInput('status', 'InProgress');
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent?.trim()).toBe('In Progress');
  });
});

describe('SeverityBadge', () => {
  it('shows "Not assessed" with the severity token', async () => {
    const fixture = TestBed.createComponent(SeverityBadge);
    fixture.componentRef.setInput('severity', 'NotAssessed');
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent?.trim()).toBe('Not assessed');
    expect(host.style.getPropertyValue('--badge-color')).toBe('var(--qa-severity-not-assessed)');
  });
});
