import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideInbox, lucideInfo, lucideSearch } from '@ng-icons/lucide';

export type EmptyStateKind = 'empty' | 'no-results' | 'error' | 'info';

const KIND_ICONS: Readonly<Record<EmptyStateKind, string>> = {
  empty: 'lucideInbox',
  'no-results': 'lucideSearch',
  error: 'lucideCircleAlert',
  info: 'lucideInfo',
};

/**
 * Centered message for empty lists, "no results" filters and error states.
 * Projected content (e.g. a "Retry" or "Add" button) is shown below the message.
 */
@Component({
  selector: 'app-empty-state',
  imports: [NgIcon],
  viewProviders: [provideIcons({ lucideInbox, lucideSearch, lucideCircleAlert, lucideInfo })],
  template: `
    <div
      class="mb-3 flex size-12 items-center justify-center rounded-full bg-surface-muted"
      [class.text-danger]="kind() === 'error'"
      [class.text-muted]="kind() !== 'error'"
    >
      <ng-icon [name]="icon()" size="24" aria-hidden="true" />
    </div>
    <h2 class="m-0 text-md font-semibold text-text">{{ title() }}</h2>
    @if (message()) {
      <p class="mt-1 mb-0 max-w-md text-sm text-muted">{{ message() }}</p>
    }
    <div class="mt-4 flex gap-2 empty:hidden"><ng-content /></div>
  `,
  host: {
    class: 'flex flex-col items-center justify-center px-6 py-12 text-center',
    '[attr.role]': 'kind() === "error" ? "alert" : null',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmptyState {
  readonly title = input.required<string>();
  readonly message = input<string>();
  readonly kind = input<EmptyStateKind>('empty');

  protected readonly icon = computed(() => KIND_ICONS[this.kind()]);
}
