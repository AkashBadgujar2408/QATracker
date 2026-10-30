import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideClockAlert } from '@ng-icons/lucide';
import { formatIst } from '../../pipes/ist-date.pipe';

const DAY_MS = 24 * 60 * 60 * 1000;

/** How long an issue is past its due date: "Overdue" (under a day) or "Overdue 3 d" (calendar days). */
export function overdueLabel(dueDate: string, now: Date = new Date()): string {
  const days = Math.floor((now.getTime() - new Date(dueDate).getTime()) / DAY_MS);
  return days >= 1 ? `Overdue ${days} d` : 'Overdue';
}

/**
 * Overdue marker for lists, cards and the issue page (ADR-038): an icon and a word in the critical tone, so colour is
 * never the only cue. Show it only for issues that are overdue (IssueStates.IsOverdue on the server).
 */
@Component({
  selector: 'app-overdue-badge',
  imports: [NgIcon],
  viewProviders: [provideIcons({ lucideClockAlert })],
  template: `<ng-icon name="lucideClockAlert" size="14" aria-hidden="true" />{{ label() }}`,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 1px 8px 1px 6px;
      border-radius: var(--qa-radius-pill);
      background: color-mix(in srgb, var(--qa-severity-critical) 12%, transparent);
      color: var(--qa-severity-critical);
      font-size: var(--qa-text-xs);
      font-weight: 600;
      line-height: 18px;
      white-space: nowrap;
    }
  `,
  host: { '[attr.title]': '"Was due " + due()' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OverdueBadge {
  /** ISO due date of the overdue issue. */
  readonly dueDate = input.required<string>();

  protected readonly label = computed(() => overdueLabel(this.dueDate()));
  protected readonly due = computed(() => formatIst(this.dueDate(), 'datetime'));
}
