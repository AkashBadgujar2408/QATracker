import { Injectable, inject } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';

/** The Testing screen's Client ▸ Project ▸ Sprint choice (plan 8.1 "remembered per user"). */
export interface TestingSelection {
  readonly clientId: number | null;
  readonly projectId: number | null;
  readonly sprintId: number | null;
}

export const EMPTY_SELECTION: TestingSelection = {
  clientId: null,
  projectId: null,
  sprintId: null,
};

/**
 * Remembers the selection in this browser, per signed-in user. Storage may be unavailable (private mode, blocked
 * site data) – then the screen simply starts empty.
 */
@Injectable({ providedIn: 'root' })
export class TestingContext {
  private readonly auth = inject(AuthService);

  load(): TestingSelection {
    try {
      const raw = localStorage.getItem(this.key());
      const value = raw ? (JSON.parse(raw) as Partial<TestingSelection>) : null;
      return {
        clientId: positive(value?.clientId),
        projectId: positive(value?.projectId),
        sprintId: positive(value?.sprintId),
      };
    } catch {
      return EMPTY_SELECTION;
    }
  }

  save(selection: TestingSelection): void {
    try {
      localStorage.setItem(this.key(), JSON.stringify(selection));
    } catch {
      // not remembered – fine
    }
  }

  /** Remembered "group by module" toggle. */
  loadGrouped(): boolean {
    try {
      return localStorage.getItem(`${this.key()}.grouped`) === '1';
    } catch {
      return false;
    }
  }

  saveGrouped(grouped: boolean): void {
    try {
      localStorage.setItem(`${this.key()}.grouped`, grouped ? '1' : '0');
    } catch {
      // not remembered – fine
    }
  }

  private key(): string {
    return `qa.testing.${this.auth.user()?.id ?? 0}`;
  }
}

function positive(value: unknown): number | null {
  return typeof value === 'number' && Number.isInteger(value) && value > 0 ? value : null;
}
