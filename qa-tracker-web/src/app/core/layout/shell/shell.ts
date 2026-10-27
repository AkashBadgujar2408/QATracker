import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Breadcrumb } from '../../../shared/ui/breadcrumb/breadcrumb';
import { AuthService } from '../../auth/auth.service';
import { BreadcrumbService } from '../breadcrumb.service';
import { LayoutService } from '../layout.service';
import { navItemsFor } from '../nav-items';
import { Sidebar } from '../sidebar/sidebar';
import { Topbar } from '../topbar/topbar';

/** Authenticated application frame: sidebar | top bar + breadcrumb + routed page. */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, Sidebar, Topbar, Breadcrumb],
  template: `
    <a
      href="#main-content"
      class="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-control focus:bg-surface focus:p-2"
      >Skip to content</a
    >
    <app-sidebar
      [items]="navItems()"
      [collapsed]="layout.sidebarCollapsed()"
      (toggleCollapsed)="layout.toggleSidebar()"
    />
    <div class="flex min-w-0 flex-1 flex-col">
      <app-topbar />
      <main id="main-content" class="flex-1 overflow-y-auto px-6 pb-8 pt-4" tabindex="-1">
        @if (breadcrumbs.items().length > 1) {
          <app-breadcrumb class="mb-2 block" [items]="breadcrumbs.items()" />
        }
        <router-outlet />
      </main>
    </div>
  `,
  host: { class: 'flex h-dvh overflow-hidden bg-bg' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Shell {
  protected readonly layout = inject(LayoutService);
  protected readonly breadcrumbs = inject(BreadcrumbService);
  private readonly auth = inject(AuthService);
  protected readonly navItems = computed(() => navItemsFor(this.auth.role()));
}
