import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, FormGroup } from '@angular/forms';
import { FakeAuth, provideFakeAuth, testUser } from '../../testing/fake-auth';
import { navItemsFor } from '../core/layout/nav-items';
import { safeReturnUrl } from '../features/auth/login-page';
import { HasRoleDirective } from './directives/has-role.directive';
import { applyServerErrors, fieldError, passwordPolicyError } from './forms/form-errors';
import { formatIst } from './pipes/ist-date.pipe';
import { PluralPipe } from './pipes/plural.pipe';

describe('formatIst', () => {
  it('formats in IST as dd/MMM/yyyy', () => {
    // 18:45 UTC = 00:15 next day in IST
    expect(formatIst('2026-10-27T18:45:00Z')).toBe('28/Oct/2026');
    expect(formatIst('2026-09-30T06:00:00Z')).toBe('30/Sep/2026');
    expect(formatIst('2026-10-27T12:30:00Z', 'datetime')).toBe('27/Oct/2026 18:00');
  });
});

describe('navItemsFor', () => {
  const labels = (role: Parameters<typeof navItemsFor>[0]) => navItemsFor(role).map((i) => i.label);

  it('filters items by role', () => {
    expect(labels('Admin')).toEqual(
      expect.arrayContaining(['Testing', 'My work', 'Masters', 'Users']),
    );
    expect(labels('Tester')).not.toContain('My work');
    expect(labels('Developer')).not.toContain('Users');
    expect(labels(null)).not.toContain('Testing');
  });
});

describe('safeReturnUrl', () => {
  it.each([
    ['/issues?x=1', '/issues?x=1'],
    [undefined, '/home'],
    ['//evil.example', '/home'],
    ['https://evil.example', '/home'],
    ['/login', '/home'],
  ])('%s -> %s', (input, expected) => expect(safeReturnUrl(input)).toBe(expected));
});

describe('form errors', () => {
  it('maps validator errors to messages', () => {
    const control = new FormControl('', { nonNullable: true });
    control.setErrors({ required: true });
    expect(fieldError(control, 'User name')).toBe('User name is required.');
  });

  it('puts API field errors on controls and returns the rest', () => {
    const form = new FormGroup({ email: new FormControl('') });
    const unmatched = applyServerErrors(form, {
      errors: { email: ['E-mail is taken.'], other: ['Something else.'] },
    });
    expect(form.controls.email.errors).toEqual({ server: 'E-mail is taken.' });
    expect(unmatched).toEqual(['Something else.']);
  });

  it.each([
    ['short1', false],
    ['longenough', false],
    ['longenough1', true],
  ])('password policy %s -> valid %s', (value, valid) => {
    expect(passwordPolicyError(new FormControl(value)) === null).toBe(valid);
  });
});

@Component({
  imports: [HasRoleDirective],
  template: `<span *appHasRole="['Admin']" class="admin-only">x</span>`,
})
class HasRoleHost {}

describe('HasRoleDirective', () => {
  it('shows content only for the roles and reacts to changes', async () => {
    TestBed.configureTestingModule({ providers: [provideFakeAuth(testUser('Tester'))] });
    const fixture = TestBed.createComponent(HasRoleHost);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('.admin-only')).toBeNull();

    TestBed.inject(FakeAuth).current.set(testUser('Admin'));
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('.admin-only')).not.toBeNull();
  });
});

describe('PluralPipe', () => {
  const plural = new PluralPipe();

  it('uses the singular only for one', () => {
    expect(plural.transform(1, 'module')).toBe('1 module');
    expect(plural.transform(0, 'module')).toBe('0 modules');
    expect(plural.transform(3, 'person', 'people')).toBe('3 people');
  });
});
