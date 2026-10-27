import { DOCUMENT, Injectable, effect, inject, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark';

export const DARK_THEME_CLASS = 'app-dark';
const STORAGE_KEY = 'qa-theme';

/** Owns the light/dark theme: toggles the `app-dark` class on <html> and remembers the choice. */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);

  readonly mode = signal<ThemeMode>(this.initialMode());

  constructor() {
    effect(() => {
      const mode = this.mode();
      this.document.documentElement.classList.toggle(DARK_THEME_CLASS, mode === 'dark');
      try {
        localStorage.setItem(STORAGE_KEY, mode);
      } catch {
        // Storage unavailable (private mode) – theme still works for this session.
      }
    });
  }

  toggle(): void {
    this.mode.update((mode) => (mode === 'dark' ? 'light' : 'dark'));
  }

  private initialMode(): ThemeMode {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored === 'light' || stored === 'dark') {
        return stored;
      }
    } catch {
      // Fall through to the OS preference.
    }
    return this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)').matches
      ? 'dark'
      : 'light';
  }
}
