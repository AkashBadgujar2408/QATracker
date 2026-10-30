import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButton, MatIconButton } from '@angular/material/button';
import {
  MatDatepicker,
  MatDatepickerInput,
  MatDatepickerToggle,
} from '@angular/material/datepicker';
import { MatFormField, MatLabel, MatSuffix } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatTooltip } from '@angular/material/tooltip';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTrash2 } from '@ng-icons/lucide';
import { problemMessage } from '../../../core/api/api';
import { ConfirmService } from '../../../core/feedback/confirm.service';
import { ToastService } from '../../../core/feedback/toast.service';
import { provideIstDateAdapter } from '../../../shared/charts/dashboard-filters';
import { IstDatePipe } from '../../../shared/pipes/ist-date.pipe';
import { EmptyState } from '../../../shared/ui/empty-state/empty-state';
import { SkeletonLines } from '../../../shared/ui/skeleton/skeleton';
import { Holiday, SettingsApi, isoDate } from './settings.api';

const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/** Holidays: not working days for due dates and working-time metrics (Sundays never are). */
@Component({
  selector: 'app-holiday-settings',
  imports: [
    FormsModule,
    MatButton,
    MatIconButton,
    MatFormField,
    MatLabel,
    MatSuffix,
    MatInput,
    MatDatepicker,
    MatDatepickerInput,
    MatDatepickerToggle,
    MatTooltip,
    NgIcon,
    IstDatePipe,
    EmptyState,
    SkeletonLines,
  ],
  providers: [provideIstDateAdapter()],
  viewProviders: [provideIcons({ lucideTrash2 })],
  template: `
    <p class="m-0 mb-5 max-w-3xl text-muted">
      Holidays don't count as working days, like Sundays. Due dates set from now on skip them;
      existing due dates stay.
    </p>

    <form class="mb-6 flex flex-wrap items-start gap-3" (ngSubmit)="add()">
      <mat-form-field class="w-48" subscriptSizing="dynamic">
        <mat-label>Date</mat-label>
        <input
          matInput
          name="date"
          required
          [matDatepicker]="picker"
          [ngModel]="date()"
          (ngModelChange)="date.set($event)"
        />
        <mat-datepicker-toggle matSuffix [for]="picker" />
        <mat-datepicker #picker />
      </mat-form-field>
      <mat-form-field class="min-w-56 flex-1 sm:max-w-sm" subscriptSizing="dynamic">
        <mat-label>Name</mat-label>
        <input
          matInput
          name="name"
          maxlength="100"
          required
          placeholder="Diwali"
          [ngModel]="name()"
          (ngModelChange)="name.set($event)"
        />
      </mat-form-field>
      <button
        matButton="filled"
        type="submit"
        class="mt-2"
        [disabled]="!date() || !name().trim() || adding()"
      >
        {{ adding() ? 'Adding…' : 'Add holiday' }}
      </button>
    </form>

    @if (holidays.isLoading() && !holidays.hasValue()) {
      <app-skeleton-lines [lines]="4" />
    } @else if (holidays.error() && !holidays.hasValue()) {
      <app-empty-state kind="error" title="Holidays could not be loaded" [message]="loadError()">
        <button matButton="outlined" type="button" (click)="holidays.reload()">Try again</button>
      </app-empty-state>
    } @else if (years().length === 0) {
      <app-empty-state
        title="No holidays yet"
        message="Add the office holidays above, so due dates skip them."
      />
    } @else {
      @for (y of years(); track y.year) {
        <section class="mb-5" [attr.aria-labelledby]="'year-' + y.year">
          <h3 [id]="'year-' + y.year" class="m-0 mb-1 text-base font-semibold text-text">
            {{ y.year }}
            <span class="font-normal text-muted">· {{ y.items.length }}</span>
          </h3>
          <ul class="qa-holidays m-0 list-none p-0">
            @for (h of y.items; track h.date) {
              <li class="flex items-center gap-4 py-2" [class.text-muted]="h.date < today">
                <span class="w-32 tabular-nums">{{ h.date | istDate }}</span>
                <span class="w-10 text-sm text-muted">{{ weekday(h.date) }}</span>
                <span class="min-w-0 flex-1 truncate">{{ h.name }}</span>
                <button
                  matIconButton
                  type="button"
                  [matTooltip]="'Remove ' + h.name"
                  [attr.aria-label]="'Remove ' + h.name"
                  (click)="remove(h)"
                >
                  <ng-icon name="lucideTrash2" size="18" />
                </button>
              </li>
            }
          </ul>
        </section>
      }
    }
  `,
  styles: `
    .qa-holidays > li + li {
      border-top: 1px solid var(--qa-border);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HolidaySettings {
  private readonly api = inject(SettingsApi);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  protected readonly holidays = rxResource({ stream: () => this.api.holidays() });
  protected readonly date = signal<Date | null>(null);
  protected readonly name = signal('');
  protected readonly adding = signal(false);
  protected readonly today = isoDate(new Date());
  protected readonly loadError = computed(() =>
    problemMessage(this.holidays.error(), 'Holidays could not be loaded.'),
  );

  /** Newest year first; dates in a year ascending. */
  protected readonly years = computed(() => {
    const byYear = new Map<string, Holiday[]>();
    for (const h of this.holidays.value() ?? []) {
      const year = h.date.slice(0, 4);
      byYear.set(year, [...(byYear.get(year) ?? []), h]);
    }
    return [...byYear.entries()]
      .sort(([a], [b]) => b.localeCompare(a))
      .map(([year, items]) => ({ year, items }));
  });

  protected add(): void {
    const date = this.date();
    const name = this.name().trim();
    if (!date || !name || this.adding()) {
      return;
    }
    this.adding.set(true);
    this.api.addHoliday(isoDate(date), name).subscribe({
      next: (holiday) => {
        this.adding.set(false);
        this.date.set(null);
        this.name.set('');
        this.holidays.update((list) =>
          [...(list ?? []), holiday].sort((a, b) => a.date.localeCompare(b.date)),
        );
        this.toast.success(`${holiday.name} added.`);
      },
      error: () => this.adding.set(false),
    });
  }

  protected async remove(holiday: Holiday): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Remove ${holiday.name}?`,
      message: 'Due dates set from now on count this day as a working day again.',
      confirmLabel: 'Remove holiday',
      danger: true,
    });
    if (!ok) {
      return;
    }
    this.api.removeHoliday(holiday.date).subscribe(() => {
      this.holidays.update((list) => (list ?? []).filter((h) => h.date !== holiday.date));
      this.toast.success(`${holiday.name} removed.`);
    });
  }

  protected weekday(date: string): string {
    const [y, m, d] = date.split('-').map(Number);
    return WEEKDAYS[new Date(y, m - 1, d).getDay()];
  }
}
