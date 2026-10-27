import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { Role } from '../models/role';

/**
 * Renders the element only for the given roles: `<button *appHasRole="['Tester', 'Admin']">…</button>`.
 * UI convenience only – the API enforces the same rules.
 */
@Directive({ selector: '[appHasRole]' })
export class HasRoleDirective {
  readonly appHasRole = input.required<readonly Role[]>();

  private readonly auth = inject(AuthService);
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);
  private shown = false;

  constructor() {
    effect(() => {
      const allowed = this.auth.hasRole(...this.appHasRole());
      if (allowed && !this.shown) {
        this.container.createEmbeddedView(this.template);
      } else if (!allowed && this.shown) {
        this.container.clear();
      }
      this.shown = allowed;
    });
  }
}
