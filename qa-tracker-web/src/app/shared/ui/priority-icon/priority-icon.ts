import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronsDown, lucideChevronsUp, lucideEqual } from '@ng-icons/lucide';
import { PRIORITY_META, Priority } from '../../models/issue-enums';

const PRIORITY_ICONS: Readonly<Record<Priority, string>> = {
  High: 'lucideChevronsUp',
  Medium: 'lucideEqual',
  Low: 'lucideChevronsDown',
};

/** Priority as an arrow icon (with accessible label; optional visible label). */
@Component({
  selector: 'app-priority-icon',
  imports: [NgIcon],
  viewProviders: [provideIcons({ lucideChevronsUp, lucideEqual, lucideChevronsDown })],
  template: `
    <ng-icon [name]="icon()" size="16" [style.color]="color()" aria-hidden="true" />
    @if (showLabel()) {
      <span>{{ meta().label }}</span>
    } @else {
      <span class="sr-only">Priority: {{ meta().label }}</span>
    }
  `,
  host: {
    class: 'inline-flex items-center gap-1 text-xs text-text',
    '[attr.title]': '"Priority: " + meta().label',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PriorityIcon {
  readonly priority = input.required<Priority>();
  readonly showLabel = input(false);

  protected readonly meta = computed(() => PRIORITY_META[this.priority()]);
  protected readonly icon = computed(() => PRIORITY_ICONS[this.priority()]);
  protected readonly color = computed(() => `var(${this.meta().colorToken})`);
}
