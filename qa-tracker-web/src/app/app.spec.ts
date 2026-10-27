import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
import { DARK_THEME_CLASS } from './core/theme/theme.service';

describe('App shell', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove(DARK_THEME_CLASS);
    TestBed.configureTestingModule({ providers: [provideRouter(routes)] });
  });

  function host(harness: RouterTestingHarness): HTMLElement {
    return harness.fixture.nativeElement as HTMLElement;
  }

  it('redirects to Home and renders sidebar, top bar and page', async () => {
    const harness = await RouterTestingHarness.create('/');

    expect(host(harness).querySelector('app-sidebar nav[aria-label="Main"]')).toBeTruthy();
    expect(host(harness).querySelector('app-topbar')).toBeTruthy();
    expect(host(harness).querySelector('h1')?.textContent).toContain('Home');
  });

  it('marks the active navigation item', async () => {
    const harness = await RouterTestingHarness.create('/issues');
    const active = host(harness).querySelector('app-sidebar a.active');

    expect(active?.textContent).toContain('Issues');
    expect(active?.getAttribute('aria-current')).toBe('page');
  });

  it('toggles the dark theme from the top bar', async () => {
    const harness = await RouterTestingHarness.create('/home');
    const toggle = host(harness).querySelector<HTMLButtonElement>(
      'app-topbar button[aria-label^="Switch to"]',
    );
    const wasDark = document.documentElement.classList.contains(DARK_THEME_CLASS);

    toggle?.click();
    await harness.fixture.whenStable();

    expect(document.documentElement.classList.contains(DARK_THEME_CLASS)).toBe(!wasDark);
  });

  it('shows the not-found page for unknown URLs', async () => {
    const harness = await RouterTestingHarness.create('/does-not-exist');
    expect(host(harness).textContent).toContain('Page not found');
  });
});
