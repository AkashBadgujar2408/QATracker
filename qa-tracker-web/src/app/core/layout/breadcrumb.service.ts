import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { ActivatedRouteSnapshot, NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';
import { BreadcrumbItem } from '../../shared/ui/breadcrumb/breadcrumb';

/** Route `data.breadcrumb` value: a label, or a full trail. */
export type RouteBreadcrumb = string | readonly BreadcrumbItem[];

/**
 * Current breadcrumb trail. Defaults to the `data.breadcrumb` of the activated routes after each navigation;
 * scoped pages (Client ▸ Project ▸ Sprint) replace it with `set()` once their data has loaded.
 */
@Injectable({ providedIn: 'root' })
export class BreadcrumbService {
  private readonly router = inject(Router);

  private readonly trail = signal<readonly BreadcrumbItem[]>([]);
  readonly items = this.trail.asReadonly();

  constructor() {
    const subscription = this.router.events
      .pipe(filter((event) => event instanceof NavigationEnd))
      .subscribe(() => this.trail.set(fromRoute(this.router.routerState.snapshot.root)));
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  set(items: readonly BreadcrumbItem[]): void {
    this.trail.set(items);
  }
}

function fromRoute(root: ActivatedRouteSnapshot): readonly BreadcrumbItem[] {
  const items: BreadcrumbItem[] = [];
  for (let route: ActivatedRouteSnapshot | null = root; route; route = route.firstChild) {
    const crumb = route.routeConfig?.data?.['breadcrumb'] as RouteBreadcrumb | undefined;
    if (typeof crumb === 'string') {
      items.push({ label: crumb });
    } else if (crumb) {
      items.push(...crumb);
    }
  }
  return items;
}
