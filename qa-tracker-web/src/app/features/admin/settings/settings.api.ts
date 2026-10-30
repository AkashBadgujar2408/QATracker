import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../../core/api/api';
import { Severity } from '../../../shared/models/issue-enums';

/** /api/settings (ADR-038). */
export interface SlaPolicy {
  readonly severity: Severity;
  readonly workingHours: number;
  readonly rowVersion: string;
}

export interface Holiday {
  /** yyyy-MM-dd */
  readonly date: string;
  readonly name: string;
}

export interface Phase {
  readonly id: number;
  readonly name: string;
  readonly sortOrder: number;
  readonly isActive: boolean;
  readonly sprintCount: number;
  readonly rowVersion: string;
}

/** A working day is 8 working hours (WorkingCalendar.HoursPerDay). */
export const HOURS_PER_DAY = 8;

/** The API's upper bound (300 working days). */
export const MAX_SLA_HOURS = 300 * HOURS_PER_DAY;

@Injectable({ providedIn: 'root' })
export class SettingsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  sla(): Observable<SlaPolicy[]> {
    return this.http.get<SlaPolicy[]>(`${this.baseUrl}/settings/sla`);
  }

  saveSla(policies: readonly SlaPolicy[]): Observable<SlaPolicy[]> {
    return this.http.put<SlaPolicy[]>(`${this.baseUrl}/settings/sla`, { policies });
  }

  holidays(): Observable<Holiday[]> {
    return this.http.get<Holiday[]>(`${this.baseUrl}/settings/holidays`);
  }

  addHoliday(date: string, name: string): Observable<Holiday> {
    return this.http.post<Holiday>(`${this.baseUrl}/settings/holidays`, { date, name });
  }

  removeHoliday(date: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/settings/holidays/${date}`);
  }

  phases(): Observable<Phase[]> {
    return this.http.get<Phase[]>(`${this.baseUrl}/settings/phases`);
  }

  addPhase(name: string): Observable<Phase> {
    return this.http.post<Phase>(`${this.baseUrl}/settings/phases`, { name });
  }

  updatePhase(phase: Phase, name: string, isActive: boolean): Observable<Phase> {
    return this.http.put<Phase>(`${this.baseUrl}/settings/phases/${phase.id}`, {
      name,
      isActive,
      rowVersion: phase.rowVersion,
    });
  }

  reorderPhases(phases: readonly Phase[]): Observable<Phase[]> {
    return this.http.put<Phase[]>(`${this.baseUrl}/settings/phases/order`, {
      phases: phases.map((p) => ({ id: p.id, rowVersion: p.rowVersion })),
    });
  }
}

/** "56 h" → "7 working days"; partial days stay in hours ("1 working day 4 h"). */
export function describeWorkingHours(hours: number): string {
  const days = Math.floor(hours / HOURS_PER_DAY);
  const rest = hours % HOURS_PER_DAY;
  const dayText = days === 0 ? '' : `${days} working day${days === 1 ? '' : 's'}`;
  const restText = rest === 0 ? '' : `${rest} h`;
  return [dayText, restText].filter(Boolean).join(' ') || '0 h';
}

/** Local calendar date as the API's yyyy-MM-dd. */
export function isoDate(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}
