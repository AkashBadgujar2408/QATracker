import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { SEVERITY_META, Severity } from '../../models/issue-enums';

/** Issue severity as a square-cornered badge (distinct shape from the status pill). */
@Component({
  selector: 'app-severity-badge',
  template: `<span class="mark" aria-hidden="true"></span>{{ meta().label }}`,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 2px 8px;
      border-radius: var(--qa-radius-control);
      border: 1px solid color-mix(in srgb, var(--badge-color) 35%, transparent);
      background: color-mix(in srgb, var(--badge-color) 10%, transparent);
      color: var(--qa-text);
      font-size: var(--qa-text-xs);
      font-weight: 500;
      line-height: 16px;
      white-space: nowrap;
    }
    .mark {
      width: 8px;
      height: 8px;
      border-radius: 2px;
      background: var(--badge-color);
    }
  `,
  host: {
    '[style.--badge-color]': 'color()',
    '[attr.title]': '"Severity: " + meta().label',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SeverityBadge {
  readonly severity = input.required<Severity>();

  protected readonly meta = computed(() => SEVERITY_META[this.severity()]);
  protected readonly color = computed(() => `var(${this.meta().colorToken})`);
}
