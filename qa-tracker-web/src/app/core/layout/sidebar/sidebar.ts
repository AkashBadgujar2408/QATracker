import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { MatTooltip } from '@angular/material/tooltip';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBriefcase,
  lucideChartPie,
  lucideChevronDown,
  lucideFlag,
  lucideFlaskConical,
  lucideFolderTree,
  lucideHouse,
  lucideList,
  lucidePanelLeftClose,
  lucidePanelLeftOpen,
  lucideRotateCcw,
  lucideSettings,
  lucideSquareKanban,
} from '@ng-icons/lucide';
import { Logo } from '../../../shared/ui/logo/logo';
import { NavItem } from '../nav-items';

/** Collapsible left navigation (icons + labels; icons + tooltips when collapsed). */
@Component({
  selector: 'app-sidebar',
  imports: [RouterLink, RouterLinkActive, NgIcon, MatTooltip, Logo],
  viewProviders: [
    provideIcons({
      lucideBriefcase,
      lucideChartPie,
      lucideChevronDown,
      lucideFlag,
      lucideFlaskConical,
      lucideFolderTree,
      lucideHouse,
      lucideList,
      lucidePanelLeftClose,
      lucidePanelLeftOpen,
      lucideRotateCcw,
      lucideSettings,
      lucideSquareKanban,
    }),
  ],
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.css',
  host: {
    class: 'flex h-full flex-col transition-[width] duration-150 motion-reduce:transition-none',
    '[style.width]': `collapsed() ? 'var(--qa-sidebar-width-collapsed)' : 'var(--qa-sidebar-width)'`,
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Sidebar {
  readonly items = input.required<readonly NavItem[]>();
  readonly collapsed = input(false);
  readonly toggleCollapsed = output();

  /** Expanded group links (e.g. '/dashboards'). */
  protected readonly openGroups = signal<ReadonlySet<string>>(new Set(['/dashboards']));

  protected toggleGroup(link: string): void {
    this.openGroups.update((open) => {
      const next = new Set(open);
      if (next.has(link)) {
        next.delete(link);
      } else {
        next.add(link);
      }
      return next;
    });
  }
}
