import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
} from '@angular/core';
import { Params, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideTriangleAlert } from '@ng-icons/lucide';

/** Change against an earlier value, in words: "+3 since last week", "No change since last week". */
export function trendText(value: number, previous: number, since: string): string {
  const delta = value - previous;
  return delta === 0 ? `No change ${since}` : `${delta > 0 ? '+' : '−'}${Math.abs(delta)} ${since}`;
}

/**
 * A headline number (plan 9.6 `app-kpi-card`): label, value and one line of context. Links to the issues behind the
 * number. `alert` marks a value that needs attention (shown with an icon, never colour alone).
 */
@Component({
  selector: 'app-kpi-card',
  imports: [RouterLink, NgIcon],
  viewProviders: [provideIcons({ lucideTriangleAlert })],
  template: `
    @if (loading()) {
      <div class="qa-skeleton h-4 w-24 rounded"></div>
      <div class="qa-skeleton mt-3 h-8 w-16 rounded"></div>
      <div class="qa-skeleton mt-3 h-3 w-32 rounded"></div>
    } @else {
      <a class="qa-kpi-link" [routerLink]="link()" [queryParams]="queryParams()">
        <span class="block text-sm text-muted">{{ label() }}</span>
        <span class="mt-1 flex items-center gap-2">
          <span class="qa-kpi-value" [class.text-severity-critical]="attention()">{{
            value()
          }}</span>
          @if (attention()) {
            <ng-icon
              name="lucideTriangleAlert"
              size="20"
              class="text-severity-critical"
              aria-label="Needs attention"
              role="img"
            />
          }
        </span>
        @if (note()) {
          <span class="mt-1 block text-sm text-muted">{{ note() }}</span>
        }
      </a>
    }
  `,
  styles: `
    :host {
      display: block;
      position: relative;
      padding: 14px 16px;
      border: 1px solid var(--qa-border);
      border-radius: var(--qa-radius-card);
      background: var(--qa-surface);
      box-shadow: var(--qa-shadow-card);
      transition: border-color 120ms;
    }
    :host(:hover) {
      border-color: var(--qa-border-strong);
    }
    :host(.qa-attention) {
      border-left: 3px solid var(--qa-severity-critical);
    }
    .qa-kpi-link {
      display: block;
      color: inherit;
      text-decoration: none;
    }
    .qa-kpi-link::after {
      content: '';
      position: absolute;
      inset: 0;
      border-radius: inherit;
    }
    .qa-kpi-link:focus-visible {
      outline: none;
    }
    :host:has(.qa-kpi-link:focus-visible) {
      outline: 2px solid var(--qa-primary);
      outline-offset: 2px;
    }
    .qa-kpi-value {
      font-size: var(--qa-text-xl);
      font-weight: 600;
      line-height: 1.15;
      font-variant-numeric: tabular-nums;
    }
  `,
  host: { '[class.qa-attention]': 'attention()' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KpiCard {
  readonly label = input.required<string>();
  readonly value = input<number | null>(null);
  /** One line of context under the number (trend, what it counts). */
  readonly note = input<string>();
  /** The value needs attention when above zero (e.g. overdue). */
  readonly alertWhenAboveZero = input(false, { transform: booleanAttribute });
  readonly loading = input(false);
  readonly link = input<string | readonly unknown[]>('/issues');
  readonly queryParams = input<Params | null>(null);

  protected readonly attention = computed(
    () => this.alertWhenAboveZero() && (this.value() ?? 0) > 0,
  );
}
