import { DOCUMENT, DestroyRef, Injectable, inject, signal } from '@angular/core';

/** Design tokens a chart needs, read from the CSS custom properties (tokens.css) – never hard-coded colours. */
export interface ChartTokens {
  readonly text: string;
  readonly muted: string;
  readonly surface: string;
  readonly border: string;
  readonly primary: string;
  readonly fontFamily: string;
  /** Any token by name, e.g. `--qa-severity-critical`. */
  readonly color: (token: string) => string;
}

/**
 * The current theme's chart tokens. Re-read when the `app-dark` class on <html> changes, so charts repaint in the
 * other theme (plan 9.6 "charts respect light / dark theme").
 */
@Injectable({ providedIn: 'root' })
export class ChartTheme {
  private readonly document = inject(DOCUMENT);
  readonly tokens = signal<ChartTokens>(this.read());

  constructor() {
    const view = this.document.defaultView;
    if (view && 'MutationObserver' in view) {
      const observer = new view.MutationObserver(() => this.tokens.set(this.read()));
      observer.observe(this.document.documentElement, {
        attributes: true,
        attributeFilter: ['class'],
      });
      inject(DestroyRef).onDestroy(() => observer.disconnect());
    }
  }

  private read(): ChartTokens {
    const root = this.document.documentElement;
    const style = this.document.defaultView?.getComputedStyle(root);
    const cache = new Map<string, string>();
    const color = (token: string): string => {
      let value = cache.get(token);
      if (value === undefined) {
        value = style?.getPropertyValue(token).trim() || '#64748b';
        cache.set(token, value);
      }
      return value;
    };
    const body = this.document.body
      ? this.document.defaultView?.getComputedStyle(this.document.body)
      : null;
    return {
      text: color('--qa-text'),
      muted: color('--qa-text-muted'),
      surface: color('--qa-surface'),
      border: color('--qa-border'),
      primary: color('--qa-primary'),
      fontFamily: body?.fontFamily || 'sans-serif',
      color,
    };
  }
}
