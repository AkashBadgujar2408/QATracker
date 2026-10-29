import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideCircleCheck, lucideTriangleAlert } from '@ng-icons/lucide';
import { Health } from '../../data/analytics.api';

/** Badge look per health (ADR-031 rule: red = critical open or > 3 overdue, amber = anything overdue). */
export const HEALTH_META: Readonly<
  Record<Health, { label: string; colorToken: string; icon: string }>
> = {
  Green: { label: 'Healthy', colorToken: '--qa-status-verified', icon: 'lucideCircleCheck' },
  Amber: { label: 'At risk', colorToken: '--qa-severity-medium', icon: 'lucideTriangleAlert' },
  Red: { label: 'Critical', colorToken: '--qa-severity-critical', icon: 'lucideCircleAlert' },
};

/** Client health as a pill with an icon and a word – the colour is never the only cue. */
@Component({
  selector: 'app-health-badge',
  imports: [NgIcon],
  viewProviders: [provideIcons({ lucideCircleAlert, lucideCircleCheck, lucideTriangleAlert })],
  template: `<ng-icon [name]="meta().icon" size="14" aria-hidden="true" />{{ meta().label }}`,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 2px 10px 2px 8px;
      border-radius: var(--qa-radius-pill);
      border: 1px solid color-mix(in srgb, var(--badge-color) 40%, transparent);
      background: color-mix(in srgb, var(--badge-color) 12%, transparent);
      color: var(--qa-text);
      font-size: var(--qa-text-xs);
      font-weight: 600;
      line-height: 18px;
      white-space: nowrap;
    }
    ng-icon {
      color: var(--badge-color);
    }
  `,
  host: {
    '[style.--badge-color]': 'color()',
    '[attr.title]': '"Health: " + meta().label',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HealthBadge {
  readonly health = input.required<Health>();

  protected readonly meta = computed(() => HEALTH_META[this.health()]);
  protected readonly color = computed(() => `var(${this.meta().colorToken})`);
}
