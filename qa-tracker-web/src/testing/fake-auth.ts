import { Provider, computed, signal } from '@angular/core';
import { of } from 'rxjs';
import { CurrentUser } from '../app/core/auth/auth.models';
import { AuthService } from '../app/core/auth/auth.service';
import { Role } from '../app/shared/models/role';

export function testUser(role: Role, overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: 1,
    userName: role.toLowerCase(),
    fullName: `Test ${role}`,
    email: `${role.toLowerCase()}@test.local`,
    role,
    mustChangePassword: false,
    avatarColor: null,
    ...overrides,
  };
}

/** Stand-in for AuthService in component tests: `providers: [provideFakeAuth(testUser('Admin'))]`. */
export class FakeAuth {
  readonly current = signal<CurrentUser | null>(null);
  readonly user = this.current.asReadonly();
  readonly isAuthenticated = computed(() => this.current() !== null);
  readonly mustChangePassword = computed(() => this.current()?.mustChangePassword === true);
  readonly role = computed(() => this.current()?.role ?? null);

  readonly logout = vi.fn(() => this.current.set(null));
  readonly expireSession = vi.fn();
  readonly refresh = vi.fn(() => of(this.current() !== null));
  readonly restoreSession = vi.fn(async () => undefined);
  readonly login = vi.fn();
  readonly changePassword = vi.fn();

  accessToken(): string | null {
    return this.current() ? 'test-token' : null;
  }

  hasRole(...roles: readonly Role[]): boolean {
    const role = this.role();
    return role !== null && roles.includes(role);
  }
}

export function provideFakeAuth(user: CurrentUser | null): Provider[] {
  const fake = new FakeAuth();
  fake.current.set(user);
  return [
    { provide: FakeAuth, useValue: fake },
    { provide: AuthService, useValue: fake },
  ];
}
