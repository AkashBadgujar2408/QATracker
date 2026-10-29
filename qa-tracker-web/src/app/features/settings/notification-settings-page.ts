import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatButton } from '@angular/material/button';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBell, lucideClock, lucideMail, lucideMailWarning } from '@ng-icons/lucide';
import { problemMessage } from '../../core/api/api';
import { ToastService } from '../../core/feedback/toast.service';
import { NOTIFICATION_ICONS, NOTIFICATION_META } from '../../core/notifications/notification-item';
import {
  EmailSettings,
  NotificationPreference,
  NotificationPreferencesApi,
} from '../../shared/data/notifications.api';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';

/**
 * E-mail choices per notification type (plan 8.3, ADR-029). In-app notifications are always on; each switch decides
 * whether the event is also mailed – right away, or in the daily summary for comments and overdue issues.
 */
@Component({
  selector: 'app-notification-settings-page',
  imports: [PageHeader, EmptyState, SkeletonLines, MatSlideToggle, MatButton, NgIcon, RouterLink],
  viewProviders: [
    provideIcons({ ...NOTIFICATION_ICONS, lucideBell, lucideClock, lucideMail, lucideMailWarning }),
  ],
  template: `
    <app-page-header
      title="Notification settings"
      subtitle="Everything shows up under the bell. Choose what should also reach your inbox."
    >
      <a actions matButton="outlined" routerLink="/notifications">
        <ng-icon name="lucideBell" size="16" class="mr-1" />
        Notifications
      </a>
    </app-page-header>

    <div class="max-w-3xl">
      @if (settings.isLoading() && !settings.hasValue()) {
        <div class="rounded-card border border-border bg-surface p-5">
          <app-skeleton-lines [lines]="8" />
        </div>
      } @else if (settings.error() && !settings.hasValue()) {
        <app-empty-state kind="error" title="Settings could not be loaded" [message]="loadError()">
          <button matButton="outlined" type="button" (click)="settings.reload()">Try again</button>
        </app-empty-state>
      } @else if (settings.hasValue()) {
        @let s = settings.value();
        @if (!s.emailEnabled) {
          <div
            class="mb-4 flex gap-3 rounded-card border border-border bg-surface-muted p-4 text-sm text-text"
            role="status"
          >
            <ng-icon name="lucideMailWarning" size="18" class="mt-0.5 shrink-0 text-muted" />
            <p class="m-0">
              E-mail is switched off for QA Tracker at the moment, so nothing is mailed. Your
              choices are kept for when an administrator switches it on.
            </p>
          </div>
        }
        <p class="m-0 mb-4 flex items-center gap-2 text-sm text-muted">
          <ng-icon name="lucideMail" size="16" />
          @if (s.email) {
            Mails go to <span class="font-medium text-text">{{ s.email }}</span>
          } @else {
            Your account has no e-mail address – ask an administrator to add one.
          }
        </p>

        @for (section of sections(); track section.title) {
          <section class="mb-6" [attr.aria-labelledby]="'pref-' + section.id">
            <h2 [id]="'pref-' + section.id" class="m-0 text-md font-semibold text-text">
              {{ section.title }}
            </h2>
            <p class="m-0 mt-0.5 mb-3 flex items-center gap-1.5 text-sm text-muted">
              @if (section.id === 'digest') {
                <ng-icon name="lucideClock" size="14" />
              }
              {{ section.hint }}
            </p>
            <ul
              class="qa-prefs m-0 list-none overflow-hidden rounded-card border border-border bg-surface p-0"
            >
              @for (item of section.items; track item.type) {
                <li class="flex items-center gap-4 px-4 py-3.5">
                  <span
                    class="qa-pref-icon flex size-9 shrink-0 items-center justify-center rounded-full"
                    [style.--tone]="tone(item)"
                    aria-hidden="true"
                  >
                    <ng-icon [name]="icon(item)" size="18" />
                  </span>
                  <div class="min-w-0 flex-1">
                    <p class="m-0 text-base font-medium text-text" [id]="'pref-label-' + item.type">
                      {{ item.label }}
                    </p>
                    <p class="m-0 mt-0.5 text-sm text-muted">{{ item.description }}</p>
                  </div>
                  <mat-slide-toggle
                    [checked]="item.email"
                    [disabled]="saving() === item.type"
                    [attr.aria-labelledby]="'pref-label-' + item.type"
                    (change)="toggle(s, item, $event.checked)"
                  >
                    <span class="sr-only">E-mail for {{ item.label }}</span>
                  </mat-slide-toggle>
                </li>
              }
            </ul>
          </section>
        }
      }
    </div>
  `,
  styles: `
    .qa-prefs > li + li {
      border-top: 1px solid var(--qa-border);
    }
    .qa-pref-icon {
      color: var(--tone);
      background: color-mix(in srgb, var(--tone) 14%, transparent);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationSettingsPage {
  private readonly api = inject(NotificationPreferencesApi);
  private readonly toast = inject(ToastService);

  protected readonly settings = rxResource({ stream: () => this.api.get() });
  protected readonly saving = signal<string | null>(null);
  protected readonly loadError = computed(() =>
    problemMessage(this.settings.error(), 'Settings could not be loaded.'),
  );

  protected readonly sections = computed(() => {
    if (!this.settings.hasValue()) {
      return [];
    }
    const s = this.settings.value();
    return [
      {
        id: 'instant',
        title: 'Mailed right away',
        hint: 'One mail per event, as it happens.',
        items: s.items.filter((i) => i.delivery === 'Instant'),
      },
      {
        id: 'digest',
        title: 'In the daily summary',
        hint: `Collected into one mail every day at ${s.digestTimeIst} IST.`,
        items: s.items.filter((i) => i.delivery === 'Digest'),
      },
    ];
  });

  protected icon(item: NotificationPreference): string {
    return NOTIFICATION_META[item.type].icon;
  }

  protected tone(item: NotificationPreference): string {
    return `var(${NOTIFICATION_META[item.type].tone})`;
  }

  protected toggle(current: EmailSettings, item: NotificationPreference, email: boolean): void {
    // Optimistic: the switch moves at once; a failure puts it back.
    this.settings.set({
      ...current,
      items: current.items.map((i) => (i.type === item.type ? { ...i, email } : i)),
    });
    this.saving.set(item.type);
    this.api.save([{ type: item.type, email }]).subscribe({
      next: (saved) => {
        this.saving.set(null);
        this.settings.set(saved);
        this.toast.success(email ? `${item.label}: mailed.` : `${item.label}: bell only.`);
      },
      // The HTTP interceptor shows the error toast.
      error: () => {
        this.saving.set(null);
        this.settings.set(current);
      },
    });
  }
}
