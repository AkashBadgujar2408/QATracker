import { Injectable, effect, signal } from '@angular/core';

const SIDEBAR_KEY = 'qa-sidebar-collapsed';

/** Shell layout state that survives reloads (per browser). */
@Injectable({ providedIn: 'root' })
export class LayoutService {
  readonly sidebarCollapsed = signal(readFlag(SIDEBAR_KEY));

  constructor() {
    effect(() => writeFlag(SIDEBAR_KEY, this.sidebarCollapsed()));
  }

  toggleSidebar(): void {
    this.sidebarCollapsed.update((collapsed) => !collapsed);
  }
}

function readFlag(key: string): boolean {
  try {
    return localStorage.getItem(key) === 'true';
  } catch {
    return false;
  }
}

function writeFlag(key: string, value: boolean): void {
  try {
    localStorage.setItem(key, String(value));
  } catch {
    // Storage unavailable – state just isn't remembered.
  }
}
