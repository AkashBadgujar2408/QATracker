import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';

@Component({
  selector: 'app-not-found-page',
  imports: [EmptyState, MatButton, RouterLink],
  template: `
    <section class="qa-card mt-6">
      <app-empty-state
        kind="no-results"
        title="Page not found"
        message="The page you are looking for does not exist or has moved."
      >
        <a matButton="filled" routerLink="/home">Go to Home</a>
      </app-empty-state>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotFoundPage {}
