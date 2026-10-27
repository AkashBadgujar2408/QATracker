import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronRight } from '@ng-icons/lucide';

export interface BreadcrumbItem {
  readonly label: string;
  /** Router link; the last item is never a link. */
  readonly link?: string;
}

/** "Client ▸ Project ▸ Sprint" trail. */
@Component({
  selector: 'app-breadcrumb',
  imports: [RouterLink, NgIcon],
  viewProviders: [provideIcons({ lucideChevronRight })],
  template: `
    <ol class="m-0 flex list-none flex-wrap items-center gap-1 p-0 text-sm text-muted">
      @for (item of items(); track $index; let last = $last) {
        <li class="flex items-center gap-1">
          @if (item.link && !last) {
            <a [routerLink]="item.link" class="text-muted no-underline hover:text-primary">{{
              item.label
            }}</a>
          } @else {
            <span [class.text-text]="last" [attr.aria-current]="last ? 'page' : null">{{
              item.label
            }}</span>
          }
          @if (!last) {
            <ng-icon name="lucideChevronRight" size="14" aria-hidden="true" />
          }
        </li>
      }
    </ol>
  `,
  host: { role: 'navigation', 'aria-label': 'Breadcrumb' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Breadcrumb {
  readonly items = input.required<readonly BreadcrumbItem[]>();
}
