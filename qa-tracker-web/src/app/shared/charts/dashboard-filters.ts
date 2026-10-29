import {
  ChangeDetectionStrategy,
  Component,
  Injectable,
  Provider,
  Signal,
  booleanAttribute,
  input,
  computed,
  inject,
} from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { DateAdapter, MAT_DATE_FORMATS, NativeDateAdapter } from '@angular/material/core';
import {
  MatDateRangeInput,
  MatDateRangePicker,
  MatDatepickerToggle,
  MatEndDate,
  MatStartDate,
} from '@angular/material/datepicker';
import { MatFormField, MatHint, MatLabel, MatSuffix } from '@angular/material/form-field';
import { MatOption, MatSelect } from '@angular/material/select';
import { ActivatedRoute, ParamMap, Params, Router } from '@angular/router';
import { catchError, of } from 'rxjs';
import { AnalyticsQuery } from '../data/analytics.api';
import { MastersApi } from '../data/masters.api';
import { SprintsApi } from '../data/sprints.api';

export type Period = '7' | '30' | '90' | 'month' | 'custom';

export const PERIODS: readonly { readonly id: Period; readonly label: string }[] = [
  { id: '7', label: 'Last 7 days' },
  { id: '30', label: 'Last 30 days' },
  { id: '90', label: 'Last 90 days' },
  { id: 'month', label: 'This month' },
  { id: 'custom', label: 'Custom range' },
];

/** The global dashboard filter (plan 9): scope and period, kept in the URL so views can be bookmarked and shared. */
export interface DashboardFilters {
  readonly clientId: number | null;
  readonly projectId: number | null;
  readonly sprintId: number | null;
  readonly period: Period;
  /** yyyy-MM-dd, custom period only. */
  readonly from: string | null;
  readonly to: string | null;
}

const ISO_DAY = /^\d{4}-\d{2}-\d{2}$/;

function id(value: string | null): number | null {
  const n = Number(value);
  return Number.isInteger(n) && n > 0 ? n : null;
}

export function dashboardFiltersFromParams(params: ParamMap): DashboardFilters {
  const from = params.get('from');
  const to = params.get('to');
  const custom = !!from && !!to && ISO_DAY.test(from) && ISO_DAY.test(to) && from <= to;
  const period = params.get('period') as Period | null;
  return {
    clientId: id(params.get('client')),
    projectId: id(params.get('project')),
    sprintId: id(params.get('sprint')),
    period: custom
      ? 'custom'
      : PERIODS.some((p) => p.id === period && p.id !== 'custom')
        ? period!
        : '30',
    from: custom ? from : null,
    to: custom ? to : null,
  };
}

export function dashboardFiltersToParams(f: DashboardFilters): Params {
  return {
    client: f.clientId,
    project: f.projectId,
    sprint: f.sprintId,
    period: f.period === '30' || f.period === 'custom' ? null : f.period,
    from: f.period === 'custom' ? f.from : null,
    to: f.period === 'custom' ? f.to : null,
  };
}

/** Today's date in IST as yyyy-MM-dd. */
export function istToday(now = new Date()): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Kolkata' }).format(now);
}

function addDays(day: string, days: number): string {
  const d = new Date(`${day}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** The API query for the filters: presets become concrete IST days. */
export function analyticsQuery(f: DashboardFilters, now = new Date()): AnalyticsQuery {
  const today = istToday(now);
  const range =
    f.period === 'custom'
      ? { from: f.from, to: f.to }
      : f.period === 'month'
        ? { from: `${today.slice(0, 8)}01`, to: today }
        : { from: addDays(today, -(Number(f.period) - 1)), to: today };
  return { clientId: f.clientId, projectId: f.projectId, sprintId: f.sprintId, ...range };
}

/** Issue-list query params for the dashboard scope – the base of every click-through. */
export function scopeParams(f: DashboardFilters): Params {
  return { client: f.clientId, project: f.projectId, sprint: f.sprintId };
}

/** The dashboard filters of the current route, as a signal. */
export function injectDashboardFilters(): Signal<DashboardFilters> {
  const route = inject(ActivatedRoute);
  const params = toSignal(route.queryParamMap, { initialValue: route.snapshot.queryParamMap });
  return computed(() => dashboardFiltersFromParams(params()));
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** Date picker in the app's format: 27/Oct/2026 (ADR-014). Parses the same, and dd/mm/yyyy. */
@Injectable()
export class IstDateAdapter extends NativeDateAdapter {
  override format(date: Date): string {
    return `${String(date.getDate()).padStart(2, '0')}/${MONTHS[date.getMonth()]}/${date.getFullYear()}`;
  }

  override parse(value: unknown): Date | null {
    if (typeof value !== 'string' || !value.trim()) {
      return null;
    }
    const match = /^(\d{1,2})[/\-. ]([A-Za-z]{3}|\d{1,2})[/\-. ](\d{4})$/.exec(value.trim());
    if (!match) {
      return this.invalid();
    }
    const month = /\d/.test(match[2])
      ? Number(match[2]) - 1
      : MONTHS.findIndex((m) => m.toLowerCase() === match[2].toLowerCase());
    const date = new Date(Number(match[3]), month, Number(match[1]));
    return month >= 0 && date.getMonth() === month ? date : this.invalid();
  }
}

export function provideIstDateAdapter(): Provider[] {
  return [
    { provide: DateAdapter, useClass: IstDateAdapter },
    {
      provide: MAT_DATE_FORMATS,
      useValue: {
        parse: { dateInput: null },
        display: {
          dateInput: null,
          monthYearLabel: { year: 'numeric', month: 'short' },
          dateA11yLabel: { year: 'numeric', month: 'long', day: 'numeric' },
          monthYearA11yLabel: { year: 'numeric', month: 'long' },
        },
      },
    },
  ];
}

function toIsoDay(date: Date | null): string | null {
  if (!date) {
    return null;
  }
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

function fromIsoDay(day: string | null): Date | null {
  if (!day) {
    return null;
  }
  const [y, m, d] = day.split('-').map(Number);
  return new Date(y, m - 1, d);
}

/**
 * The one filter row above every dashboard (plan 9): Client, Project, Sprint, Period (with a custom range). Writes to
 * the URL; pages read it with `injectDashboardFilters()`.
 */
@Component({
  selector: 'app-dashboard-filters',
  imports: [
    MatFormField,
    MatLabel,
    MatHint,
    MatSuffix,
    MatSelect,
    MatOption,
    MatDateRangeInput,
    MatStartDate,
    MatEndDate,
    MatDateRangePicker,
    MatDatepickerToggle,
  ],
  providers: [provideIstDateAdapter()],
  template: `
    <div class="qa-filter-row" role="group" aria-label="Dashboard filters">
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Client</mat-label>
        <mat-select
          placeholder="All clients"
          [value]="filters().clientId"
          (selectionChange)="update({ clientId: $event.value, projectId: null, sprintId: null })"
        >
          <mat-option [value]="null">All clients</mat-option>
          @for (c of cascade.value()?.clients ?? []; track c.id) {
            <mat-option [value]="c.id">{{ c.name }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Project</mat-label>
        <mat-select
          placeholder="All projects"
          [value]="filters().projectId"
          [disabled]="!filters().clientId"
          (selectionChange)="update({ projectId: $event.value, sprintId: null })"
        >
          <mat-option [value]="null">All projects</mat-option>
          @for (p of cascade.value()?.projects ?? []; track p.id) {
            <mat-option [value]="p.id">{{ p.name }} ({{ p.key }})</mat-option>
          }
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Sprint</mat-label>
        <mat-select
          placeholder="All sprints"
          [value]="filters().sprintId"
          [disabled]="!filters().projectId"
          (selectionChange)="update({ sprintId: $event.value })"
        >
          <mat-option [value]="null">All sprints</mat-option>
          @for (s of sprints.value() ?? []; track s.id) {
            <mat-option [value]="s.id">{{ s.name }}</mat-option>
          }
        </mat-select>
      </mat-form-field>
      @if (showPeriod()) {
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Period</mat-label>
          <mat-select [value]="filters().period" (selectionChange)="choosePeriod($event.value)">
            @for (p of periods; track p.id) {
              <mat-option [value]="p.id">{{ p.label }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        @if (filters().period === 'custom') {
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="qa-range">
            <mat-label>From – to</mat-label>
            <mat-date-range-input [rangePicker]="picker" [max]="today">
              <input
                matStartDate
                placeholder="27/Oct/2026"
                [value]="fromDate()"
                (dateChange)="pending.from = $event.value"
              />
              <input
                matEndDate
                placeholder="29/Oct/2026"
                [value]="toDate()"
                (dateChange)="setRange(pending.from, $event.value)"
              />
            </mat-date-range-input>
            <mat-hint>dd/MMM/yyyy</mat-hint>
            <mat-datepicker-toggle matIconSuffix [for]="picker" />
            <mat-date-range-picker #picker />
          </mat-form-field>
        }
      }
    </div>
  `,
  styles: `
    .qa-filter-row {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
      gap: 12px;
      --mat-form-field-container-height: 40px;
      --mat-form-field-container-vertical-padding: 8px;
    }
    .qa-range {
      grid-column: span 2;
    }
    @media (max-width: 480px) {
      .qa-range {
        grid-column: auto;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardFilterBar {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly masters = inject(MastersApi);
  private readonly sprintsApi = inject(SprintsApi);

  /** Off for dashboards of one sprint (the sprint is the period). */
  readonly showPeriod = input(true, { transform: booleanAttribute });
  protected readonly periods = PERIODS;
  protected readonly today = new Date();
  protected readonly filters = injectDashboardFilters();
  protected readonly pending: { from: Date | null } = { from: null };

  protected readonly fromDate = computed(() => fromIsoDay(this.filters().from));
  protected readonly toDate = computed(() => fromIsoDay(this.filters().to));

  protected readonly cascade = rxResource({
    params: () => ({ clientId: this.filters().clientId }),
    stream: ({ params }) =>
      this.masters.cascade(params.clientId, null).pipe(catchError(() => of(null))),
  });
  protected readonly sprints = rxResource({
    params: () => this.filters().projectId ?? undefined,
    stream: ({ params }) => this.sprintsApi.list(params).pipe(catchError(() => of([]))),
  });

  protected update(patch: Partial<DashboardFilters>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: dashboardFiltersToParams({ ...this.filters(), ...patch }),
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  protected choosePeriod(period: Period): void {
    if (period === 'custom') {
      const q = analyticsQuery(this.filters());
      this.update({ period, from: q.from ?? null, to: q.to ?? null });
    } else {
      this.update({ period, from: null, to: null });
    }
  }

  protected setRange(from: Date | null, to: Date | null): void {
    const start = toIsoDay(from ?? this.fromDate());
    const end = toIsoDay(to);
    if (start && end && start <= end) {
      this.update({ period: 'custom', from: start, to: end });
    }
  }
}
