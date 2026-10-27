import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ISSUE_STATUS_META, IssueStatus } from '../../models/issue-enums';

/** Issue status as a rounded pill: tinted background + coloured dot + label (never colour alone). */
@Component({
  selector: 'app-status-pill',
  template: `<span class="dot" aria-hidden="true"></span>{{ meta().label }}`,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 2px 10px;
      border-radius: var(--qa-radius-pill);
      background: color-mix(in srgb, var(--pill-color) 14%, transparent);
      color: var(--qa-text);
      font-size: var(--qa-text-xs);
      font-weight: 500;
      line-height: 18px;
      white-space: nowrap;
    }
    .dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: var(--pill-color);
    }
  `,
  host: {
    '[style.--pill-color]': 'color()',
    '[attr.title]': '"Status: " + meta().label',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusPill {
  readonly status = input.required<IssueStatus>();

  protected readonly meta = computed(() => ISSUE_STATUS_META[this.status()]);
  protected readonly color = computed(() => `var(${this.meta().colorToken})`);
}
