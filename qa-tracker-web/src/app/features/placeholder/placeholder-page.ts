import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { map } from 'rxjs';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/** Route data for pages that are not built yet. */
export interface PlaceholderData {
  readonly heading: string;
  /** Build session that delivers the page, e.g. 'S11'. */
  readonly session: string;
}

/** Stand-in page so every navigation item works before its feature session is built. */
@Component({
  selector: 'app-placeholder-page',
  imports: [PageHeader, EmptyState],
  template: `
    <app-page-header [title]="page().heading" />
    <section class="qa-card">
      <app-empty-state
        kind="info"
        title="Coming soon"
        [message]="'This page is built in session ' + page().session + ' of the build plan.'"
      />
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlaceholderPage {
  protected readonly page = toSignal(
    inject(ActivatedRoute).data.pipe(map((data) => data['placeholder'] as PlaceholderData)),
    { requireSync: true },
  );
}
