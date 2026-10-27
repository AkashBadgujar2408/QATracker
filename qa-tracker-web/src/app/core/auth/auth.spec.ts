import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { testUser } from '../../../testing/fake-auth';
import { AuthResponse } from './auth.models';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

function response(token: string, mustChangePassword = false): AuthResponse {
  return {
    accessToken: token,
    accessTokenExpiresAt: '2026-10-27T10:15:00Z',
    user: testUser('Tester', { mustChangePassword }),
  };
}

describe('AuthService + authInterceptor', () => {
  let http: HttpTestingController;
  let auth: AuthService;
  let client: HttpClient;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    client = TestBed.inject(HttpClient);
  });

  afterEach(() => http.verify());

  it('logs in with user name, sends rememberMe and keeps the token in memory', async () => {
    const login = firstValueFrom(auth.login('akash', 'Secret123', true));

    const request = http.expectOne('/api/auth/login');
    expect(request.request.body).toEqual({
      userName: 'akash',
      password: 'Secret123',
      rememberMe: true,
    });
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush(response('token-1'));

    expect((await login).role).toBe('Tester');
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.accessToken()).toBe('token-1');
    expect(auth.hasRole('Tester', 'Admin')).toBe(true);
    expect(auth.hasRole('Admin')).toBe(false);
  });

  it('adds the bearer token to API calls', async () => {
    void firstValueFrom(auth.login('akash', 'Secret123', false));
    http.expectOne('/api/auth/login').flush(response('token-1'));

    void firstValueFrom(client.get('/api/users'));
    expect(http.expectOne('/api/users').request.headers.get('Authorization')).toBe(
      'Bearer token-1',
    );
  });

  it('refreshes once on 401 and retries the request with the new token', async () => {
    void firstValueFrom(auth.login('akash', 'Secret123', false));
    http.expectOne('/api/auth/login').flush(response('old'));

    const result = firstValueFrom(client.get<string[]>('/api/users'));
    http.expectOne('/api/users').flush(null, { status: 401, statusText: 'Unauthorized' });
    http.expectOne('/api/auth/refresh').flush(response('new'));
    const retry = http.expectOne('/api/users');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new');
    retry.flush(['ok']);

    expect(await result).toEqual(['ok']);
  });

  it('shares one refresh between parallel 401s', () => {
    void firstValueFrom(auth.login('akash', 'Secret123', false));
    http.expectOne('/api/auth/login').flush(response('old'));

    void firstValueFrom(client.get('/api/a'));
    void firstValueFrom(client.get('/api/b'));
    http.expectOne('/api/a').flush(null, { status: 401, statusText: 'Unauthorized' });
    http.expectOne('/api/b').flush(null, { status: 401, statusText: 'Unauthorized' });

    http.expectOne('/api/auth/refresh').flush(response('new'));
    http.expectOne('/api/a').flush({});
    http.expectOne('/api/b').flush({});
  });

  it('ends the session when the refresh fails', async () => {
    void firstValueFrom(auth.login('akash', 'Secret123', false));
    http.expectOne('/api/auth/login').flush(response('old'));
    const expire = vi.spyOn(auth, 'expireSession').mockImplementation(() => undefined);

    const result = firstValueFrom(client.get('/api/users'));
    http.expectOne('/api/users').flush(null, { status: 401, statusText: 'Unauthorized' });
    http.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(result).rejects.toBeTruthy();
    expect(expire).toHaveBeenCalled();
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('restoreSession resumes from the refresh cookie and never throws', async () => {
    const restored = auth.restoreSession();
    http.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });
    await restored;
    expect(auth.isAuthenticated()).toBe(false);

    const again = auth.restoreSession();
    http.expectOne('/api/auth/refresh').flush(response('t', true));
    await again;
    expect(auth.mustChangePassword()).toBe(true);
  });
});
