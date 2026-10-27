import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { DARK_THEME_CLASS, ThemeService } from './core/theme/theme.service';

describe('App', () => {
  beforeEach(async () => {
    localStorage.clear();
    document.documentElement.classList.remove(DARK_THEME_CLASS);
    await TestBed.configureTestingModule({ imports: [App] }).compileComponents();
  });

  it('renders the app title', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('QA Tracker');
  });

  it('toggles the dark theme class on <html>', async () => {
    const fixture = TestBed.createComponent(App);
    const theme = TestBed.inject(ThemeService);
    theme.mode.set('light');
    await fixture.whenStable();
    expect(document.documentElement.classList.contains(DARK_THEME_CLASS)).toBe(false);

    theme.toggle();
    await fixture.whenStable();
    expect(document.documentElement.classList.contains(DARK_THEME_CLASS)).toBe(true);
  });
});
