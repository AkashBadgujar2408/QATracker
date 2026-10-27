import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideFakeAuth, testUser } from '../../../../testing/fake-auth';
import { User } from './users.api';
import { UsersPage } from './users-page';

const nitin: User = {
  id: 2,
  userName: 'nitin',
  fullName: 'Nitin Patil',
  email: 'nitin@test.local',
  role: 'Developer',
  isActive: true,
  mustChangePassword: true,
  avatarColor: null,
  lastLoginAt: null,
  createdAt: '2026-10-27T10:00:00Z',
  rowVersion: 'AAAAAAAAB9E=',
};

describe('UsersPage', () => {
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

  async function render() {
    const fixture = TestBed.createComponent(UsersPage);
    // Not whenStable(): the pending users request keeps the app unstable until the test flushes it.
    TestBed.tick();
    return fixture;
  }

  it('loads active users and shows them in the table', async () => {
    const fixture = await render();

    const request = http.expectOne((r) => r.url === '/api/users');
    expect(request.request.params.get('isActive')).toBe('true');
    request.flush([nitin]);
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Nitin Patil');
    expect(text).toContain('Developer');
    expect(text).toContain('Must change password');
    expect(text).toContain('Never');
  });

  it('shows the empty state when there are no users', async () => {
    const fixture = await render();
    http.expectOne((r) => r.url === '/api/users').flush([]);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No users yet');
  });

  it('shows an error state with retry', async () => {
    const fixture = await render();
    http
      .expectOne((r) => r.url === '/api/users')
      .flush({ detail: 'Database offline.' }, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Could not load users');
    expect(host.textContent).toContain('Database offline.');
  });
});
