import { Injectable, Injector, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { goTargetsFor, isTyping, shortcutGroupsFor } from './shortcuts';

/** Element id of the top bar's issue search ("/" focuses it). */
export const GLOBAL_SEARCH_ID = 'qa-global-search';

/** How long "g" waits for the second key. */
const CHORD_MS = 1500;

/** Any open dialog (Material dialogs live in the CDK overlay container). */
function dialogOpen(): boolean {
  return document.querySelector('.mat-mdc-dialog-container') !== null;
}

/**
 * App-wide keys (the shell forwards document keydown here): "?" help, "/" search, "g" + letter to a page.
 * Ignored while typing, with Ctrl/Alt/⌘ held, or while a dialog is open (page keys such as "n" live on their page).
 */
@Injectable({ providedIn: 'root' })
export class KeyboardShortcuts {
  private readonly injector = inject(Injector);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private chordStartedAt: number | null = null;

  async openHelp(): Promise<void> {
    const { openShortcutsHelp } = await import('./open-shortcuts-help');
    openShortcutsHelp(this.injector, shortcutGroupsFor(this.auth.role()));
  }

  /** Returns true when the key was a shortcut (and handled). */
  handle(event: KeyboardEvent, now = Date.now()): boolean {
    if (event.ctrlKey || event.metaKey || event.altKey || event.defaultPrevented) {
      return false;
    }
    if (isTyping(event.target) || dialogOpen()) {
      this.chordStartedAt = null;
      return false;
    }

    const key = event.key;
    const chord = this.chordStartedAt !== null && now - this.chordStartedAt <= CHORD_MS;
    this.chordStartedAt = null;

    if (chord) {
      const target = goTargetsFor(this.auth.role()).find((t) => t.key === key.toLowerCase());
      if (target) {
        event.preventDefault();
        void this.router.navigateByUrl(target.link);
        return true;
      }
      return false;
    }

    switch (key) {
      case '?':
        event.preventDefault();
        void this.openHelp();
        return true;
      case '/': {
        const search = document.getElementById(GLOBAL_SEARCH_ID);
        if (search) {
          event.preventDefault();
          search.focus();
          return true;
        }
        return false;
      }
      case 'g':
        this.chordStartedAt = now;
        return true;
      default:
        return false;
    }
  }
}
