import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { SPRINT_STATUS_LABELS, SprintStatus } from '../../data/sprints.api';

const COLORS: Readonly<Record<SprintStatus, string>> = {
  Testing: 'var(--qa-sprint-testing)',
  TestingCompleted: 'var(--qa-sprint-testing-completed)',
  Closed: 'var(--qa-sprint-closed)',
};

/** Sprint state (plan 10.2 "Sprint badges"): outlined chip with a dot and the label – never colour alone. */
@Component({
  selector: 'app-sprint-status',
  template: `<span class="dot" aria-hidden="true"></span>{{ label() }}`,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 2px 10px;
      border-radius: var(--qa-radius-pill);
      border: 1px solid var(--chip-color);
      color: var(--chip-color);
      font-size: var(--qa-text-sm);
      font-weight: 500;
      line-height: 20px;
      white-space: nowrap;
    }
    .dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: currentColor;
    }
  `,
  host: { '[style.--chip-color]': 'color()', '[attr.title]': '"Sprint: " + label()' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SprintStatusBadge {
  readonly status = input.required<SprintStatus>();

  protected readonly label = computed(() => SPRINT_STATUS_LABELS[this.status()]);
  protected readonly color = computed(() => COLORS[this.status()]);
}
