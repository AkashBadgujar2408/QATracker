import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Page title row: title (+ optional subtitle) on the left, primary action(s) on the right.
 * Usage: <app-page-header title="Issues"><button actions matButton="filled">New issue</button></app-page-header>
 */
@Component({
  selector: 'app-page-header',
  template: `
    <div class="min-w-0">
      <h1 class="m-0 truncate text-xl font-semibold leading-tight tracking-tight text-text">
        {{ title() }}
      </h1>
      @if (subtitle()) {
        <p class="mt-1 mb-0 text-sm text-muted">{{ subtitle() }}</p>
      }
    </div>
    <div class="flex shrink-0 items-center gap-2">
      <ng-content select="[actions]" />
    </div>
  `,
  host: { class: 'mb-6 flex items-start justify-between gap-4' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
}
