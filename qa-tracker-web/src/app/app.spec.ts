import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { FakeAuth, provideFakeAuth, testUser } from '../testing/fake-auth';
import { routes } from './app.routes';
import { CurrentUser } from './core/auth/auth.models';
import { DARK_THEME_CLASS } from './core/theme/theme.service';

describe('App shell', () => {
  function setup(user: CurrentUser | null) {
    localStorage.clear();
    document.documentElement.classList.remove(DARK_THEME_CLASS);
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeAuth(user),
      ],
    });
  }

  function host(harness: RouterTestingHarness): HTMLElement {
    return harness.fixture.nativeElement as HTMLElement;
  }

  function navLabels(harness: RouterTestingHarness): string[] {
    return Array.from(
      host(harness).querySelectorAll('app-sidebar nav a, app-sidebar nav button'),
    ).map((el) => el.textContent?.trim() ?? '');
  }

  it('sends signed-out visitors to the login page with a return URL', async () => {
    setup(null);
    const harness = await RouterTestingHarness.create('/issues');

    expect(harness.routeNativeElement?.textContent).toContain('Sign in');
    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Fissues');
  });

  it('redirects to Home and renders sidebar, top bar and page', async () => {
    setup(testUser('Admin'));
    const harness = await RouterTestingHarness.create('/');

    expect(host(harness).querySelector('app-sidebar nav[aria-label="Main"]')).toBeTruthy();
    expect(host(harness).querySelector('app-topbar')).toBeTruthy();
    expect(host(harness).querySelector('h1')?.textContent).toContain('Overview'); // Home = the overview dashboard
  });

  it('shows navigation by role', async () => {
    setup(testUser('Developer'));
    const harness = await RouterTestingHarness.create('/no-such-page'); // a page without data requests (not found)
    const developer = navLabels(harness);
    expect(developer).toContain('My work');
    expect(developer).not.toContain('Testing');
    expect(developer).not.toContain('Users');

    TestBed.inject(FakeAuth).current.set(testUser('Tester'));
    await harness.fixture.whenStable();
    const tester = navLabels(harness);
    expect(tester).toContain('Testing');
    expect(tester).toContain('Masters');
    expect(tester).not.toContain('My work');
  });

  it('sends users without the role Home', async () => {
    setup(testUser('Developer'));
    const harness = await RouterTestingHarness.create('/admin/users');
    expect(host(harness).querySelector('h1')?.textContent).toContain('Overview'); // Home = the overview dashboard
  });

  it('forces a password change before entering the app', async () => {
    setup(testUser('Tester', { mustChangePassword: true }));
    const harness = await RouterTestingHarness.create('/issues');
    expect(harness.routeNativeElement?.textContent).toContain('Choose a new password');
  });

  it('marks the active navigation item', async () => {
    setup(testUser('Tester'));
    const harness = await RouterTestingHarness.create('/issues');
    const active = host(harness).querySelector('app-sidebar a.active');

    expect(active?.textContent).toContain('Issues');
    expect(active?.getAttribute('aria-current')).toBe('page');
  });

  it('toggles the dark theme from the top bar', async () => {
    setup(testUser('Tester'));
    const harness = await RouterTestingHarness.create('/no-such-page'); // a page without data requests (not found)
    const toggle = host(harness).querySelector<HTMLButtonElement>(
      'app-topbar button[aria-label^="Switch to"]',
    );
    const wasDark = document.documentElement.classList.contains(DARK_THEME_CLASS);

    toggle?.click();
    await harness.fixture.whenStable();

    expect(document.documentElement.classList.contains(DARK_THEME_CLASS)).toBe(!wasDark);
  });

  it('shows the not-found page for unknown URLs', async () => {
    setup(testUser('Tester'));
    const harness = await RouterTestingHarness.create('/does-not-exist');
    expect(host(harness).textContent).toContain('Page not found');
  });
});
