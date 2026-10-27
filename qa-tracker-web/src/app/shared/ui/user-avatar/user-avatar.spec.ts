import { TestBed } from '@angular/core/testing';
import { UserAvatar, initialsOf, paletteSlotOf } from './user-avatar';

describe('UserAvatar', () => {
  it.each([
    ['Akash Badgujar', 'AB'],
    ['nitin', 'N'],
    ['  Rahul  Kumar Sharma ', 'RS'],
    ['', '?'],
  ])('initialsOf(%j) = %s', (name, expected) => {
    expect(initialsOf(name)).toBe(expected);
  });

  it('gives a stable palette slot between 1 and 8', () => {
    const slot = paletteSlotOf('Akash Badgujar');
    expect(slot).toBeGreaterThanOrEqual(1);
    expect(slot).toBeLessThanOrEqual(8);
    expect(paletteSlotOf('akash badgujar ')).toBe(slot);
  });

  it('renders initials with an accessible name and token colour', async () => {
    const fixture = TestBed.createComponent(UserAvatar);
    fixture.componentRef.setInput('name', 'Akash Badgujar');
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent?.trim()).toBe('AB');
    expect(host.getAttribute('aria-label')).toBe('Akash Badgujar');
    expect(host.style.background).toContain('var(--qa-avatar-');
  });

  it('uses the explicit colour when given', async () => {
    const fixture = TestBed.createComponent(UserAvatar);
    fixture.componentRef.setInput('name', 'Nitin');
    fixture.componentRef.setInput('color', 'var(--qa-avatar-3)');
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).style.background).toBe('var(--qa-avatar-3)');
  });
});
