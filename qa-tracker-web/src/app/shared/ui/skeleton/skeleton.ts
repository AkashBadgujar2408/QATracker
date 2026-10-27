import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

const LINE_WIDTHS = ['100%', '92%', '76%', '84%', '64%'];

/** Placeholder text lines while content loads. */
@Component({
  selector: 'app-skeleton-lines',
  template: `
    @for (width of widths(); track $index) {
      <div class="qa-skeleton h-3.5" [style.width]="width"></div>
    }
  `,
  host: { class: 'flex flex-col gap-2', 'aria-busy': 'true', 'aria-label': 'Loading' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SkeletonLines {
  readonly lines = input(3);

  protected readonly widths = computed(() =>
    Array.from({ length: this.lines() }, (_, i) => LINE_WIDTHS[i % LINE_WIDTHS.length]),
  );
}

/** Placeholder table (header + rows) while a list loads. */
@Component({
  selector: 'app-skeleton-table',
  template: `
    <div class="flex gap-4 border-b border-border px-4 py-3">
      @for (col of columnIndexes(); track col) {
        <div class="qa-skeleton h-3 flex-1"></div>
      }
    </div>
    @for (row of rowIndexes(); track row) {
      <div class="flex gap-4 border-b border-border px-4 py-3 last:border-b-0">
        @for (col of columnIndexes(); track col) {
          <div class="qa-skeleton h-3.5 flex-1" [style.max-width]="col === 0 ? '96px' : null"></div>
        }
      </div>
    }
  `,
  host: { class: 'qa-card block overflow-hidden', 'aria-busy': 'true', 'aria-label': 'Loading' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SkeletonTable {
  readonly rows = input(5);
  readonly columns = input(4);

  protected readonly rowIndexes = computed(() => Array.from({ length: this.rows() }, (_, i) => i));
  protected readonly columnIndexes = computed(() =>
    Array.from({ length: this.columns() }, (_, i) => i),
  );
}
