import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, firstValueFrom, map, of, shareReplay, tap } from 'rxjs';
import { Role } from '../../shared/models/role';
import { API_BASE_URL, SILENT_ERRORS } from '../api/api';
import { AuthResponse, CurrentUser } from './auth.models';

interface Session {
  readonly accessToken: string;
  readonly user: CurrentUser;
}

/**
 * Auth store (signals). The access token lives only in memory; the refresh token is an httpOnly cookie the
 * browser sends to /api/auth/*. On startup `restoreSession()` silently refreshes, so a reload keeps you signed in.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly baseUrl = inject(API_BASE_URL);

  private readonly session = signal<Session | null>(null);
  private refreshInFlight: Observable<boolean> | null = null;

  readonly user = computed(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly mustChangePassword = computed(() => this.user()?.mustChangePassword === true);
  readonly role = computed(() => this.user()?.role ?? null);

  accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  hasRole(...roles: readonly Role[]): boolean {
    const role = this.role();
    return role !== null && roles.includes(role);
  }

  login(userName: string, password: string, rememberMe: boolean): Observable<CurrentUser> {
    return this.http
      .post<AuthResponse>(
        `${this.baseUrl}/auth/login`,
        { userName, password, rememberMe },
        { withCredentials: true, context: silent() },
      )
      .pipe(map((response) => this.start(response)));
  }

  changePassword(currentPassword: string, newPassword: string): Observable<CurrentUser> {
    return this.http
      .post<AuthResponse>(
        `${this.baseUrl}/auth/change-password`,
        { currentPassword, newPassword },
        { withCredentials: true, context: silent() },
      )
      .pipe(map((response) => this.start(response)));
  }

  /** New access token from the refresh cookie. Concurrent callers share one request. Emits false when signed out. */
  refresh(): Observable<boolean> {
    this.refreshInFlight ??= this.http
      .post<AuthResponse>(`${this.baseUrl}/auth/refresh`, null, {
        withCredentials: true,
        context: silent(),
      })
      .pipe(
        tap((response) => this.start(response)),
        map(() => true),
        catchError(() => {
          this.session.set(null);
          return of(false);
        }),
        finalize(() => (this.refreshInFlight = null)),
        shareReplay(1),
      );
    return this.refreshInFlight;
  }

  /** App start: try to resume the session from the refresh cookie (never throws). */
  async restoreSession(): Promise<void> {
    await firstValueFrom(this.refresh());
  }

  logout(): void {
    this.http
      .post<void>(`${this.baseUrl}/auth/logout`, null, { withCredentials: true, context: silent() })
      .pipe(catchError(() => of(undefined)))
      .subscribe(() => {
        this.session.set(null);
        void this.router.navigateByUrl('/login');
      });
  }

  /** Called when the API rejects the session (refresh failed): go to login and come back afterwards. */
  expireSession(): void {
    this.session.set(null);
    const returnUrl = this.router.url;
    void this.router.navigate(['/login'], {
      queryParams: returnUrl && !returnUrl.startsWith('/login') ? { returnUrl } : {},
    });
  }

  private start(response: AuthResponse): CurrentUser {
    this.session.set({ accessToken: response.accessToken, user: response.user });
    return response.user;
  }
}

function silent(): HttpContext {
  return new HttpContext().set(SILENT_ERRORS, true);
}
