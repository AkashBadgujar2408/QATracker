import {
  ChangeDetectionStrategy,
  Component,
  Directive,
  computed,
  input,
  output,
  signal,
} from '@angular/core';
import { MatButton } from '@angular/material/button';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChartColumn, lucideTable } from '@ng-icons/lucide';
import type { ECElementEvent, EChartsCoreOption } from 'echarts/core';
import { NgxEchartsDirective } from 'ngx-echarts';
import { ChartSelection } from './chart-options';

/** The same numbers as a table – every chart has one (accessibility, copying numbers). */
export interface ChartTable {
  readonly columns: readonly string[];
  readonly rows: readonly (readonly (string | number)[])[];
}

/** Inputs every chart shares: title, one-line subtitle, states and height (plan 9 "every chart has…"). */
@Directive()
export abstract class ChartInputs {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
  readonly loading = input(false);
  /** A message when the data could not be loaded. */
  readonly error = input<string | null>(null);
  /** Shown instead of an empty plot. */
  readonly emptyMessage = input('Nothing to show for these filters.');
  readonly height = input(280);
  /** Click-through: the clicked item/category key and series key. */
  readonly selected = output<ChartSelection>();
  /** Retry after an error. */
  readonly retry = output<void>();
}

/**
 * Chart card: title, subtitle, loading skeleton, error and empty states, a table view and the ECharts canvas.
 * The wrappers (`app-chart-bar`, …) build the options; this component only presents them.
 */
@Component({
  selector: 'app-chart',
  imports: [MatButton, NgIcon, NgxEchartsDirective],
  viewProviders: [provideIcons({ lucideChartColumn, lucideTable })],
  template: `
    <header class="mb-3 flex items-start gap-3">
      <div class="min-w-0 flex-1">
        <h2 class="m-0 text-md font-semibold text-text">{{ title() }}</h2>
        @if (subtitle()) {
          <p class="m-0 mt-0.5 text-sm text-muted">{{ subtitle() }}</p>
        }
      </div>
      @if (hasData() && table()) {
        <button
          type="button"
          class="qa-view-toggle"
          [attr.aria-pressed]="showTable()"
          [attr.aria-label]="showTable() ? 'Show chart' : 'Show as table'"
          [title]="showTable() ? 'Show chart' : 'Show as table'"
          (click)="showTable.set(!showTable())"
        >
          <ng-icon [name]="showTable() ? 'lucideChartColumn' : 'lucideTable'" size="16" />
        </button>
      }
    </header>
    <div class="qa-chart-body" [style.min-height.px]="height()">
      @if (loading()) {
        <div
          class="qa-skeleton w-full rounded-card"
          [style.height.px]="height()"
          aria-busy="true"
        ></div>
      } @else if (error()) {
        <div class="qa-chart-state" role="alert" [style.height.px]="height()">
          <p class="m-0 text-severity-critical">{{ error() }}</p>
          <button matButton="outlined" type="button" (click)="retry.emit()">Try again</button>
        </div>
      } @else if (!hasData()) {
        <div class="qa-chart-state text-muted" [style.height.px]="height()">
          <p class="m-0">{{ emptyMessage() }}</p>
        </div>
      } @else if (showTable() && table(); as t) {
        <div class="overflow-auto" [style.max-height.px]="height()">
          <table class="qa-chart-table">
            <thead>
              <tr>
                @for (c of t.columns; track $index) {
                  <th scope="col">{{ c }}</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of t.rows; track $index) {
                <tr>
                  @for (cell of row; track $index) {
                    @if ($first) {
                      <th scope="row">{{ cell }}</th>
                    } @else {
                      <td>{{ cell }}</td>
                    }
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      } @else {
        <div
          echarts
          class="w-full"
          [style.height.px]="height()"
          [options]="options()"
          [autoResize]="true"
          [class.qa-clickable]="clickable()"
          (chartClick)="onClick($event)"
        ></div>
      }
    </div>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      min-width: 0;
      height: 100%;
      padding: 16px 16px 12px;
      border: 1px solid var(--qa-border);
      border-radius: var(--qa-radius-card);
      background: var(--qa-surface);
      box-shadow: var(--qa-shadow-card);
    }
    .qa-chart-body {
      flex: 1;
    }
    .qa-chart-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 12px;
      text-align: center;
    }
    .qa-view-toggle {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 32px;
      height: 32px;
      flex-shrink: 0;
      border: 1px solid var(--qa-border);
      border-radius: var(--qa-radius-control);
      background: transparent;
      color: var(--qa-text-muted);
      cursor: pointer;
    }
    .qa-view-toggle:hover {
      color: var(--qa-text);
      border-color: var(--qa-border-strong);
    }
    .qa-view-toggle:focus-visible {
      outline: 2px solid var(--qa-primary);
      outline-offset: 2px;
    }
    .qa-clickable {
      cursor: pointer;
    }
    .qa-chart-table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--qa-text-sm);
      font-variant-numeric: tabular-nums;
    }
    .qa-chart-table th,
    .qa-chart-table td {
      padding: 6px 8px;
      border-bottom: 1px solid var(--qa-border);
      text-align: right;
    }
    .qa-chart-table th:first-child,
    .qa-chart-table thead th:first-child {
      text-align: left;
      font-weight: 500;
    }
    .qa-chart-table thead th {
      color: var(--qa-text-muted);
      font-weight: 500;
    }
  `,
  host: { role: 'figure', '[attr.aria-label]': 'title()' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Chart {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
  readonly options = input.required<EChartsCoreOption>();
  readonly table = input<ChartTable | null>(null);
  readonly loading = input(false);
  readonly error = input<string | null>(null);
  readonly empty = input(false);
  readonly emptyMessage = input('Nothing to show for these filters.');
  readonly height = input(280);
  readonly clickable = input(true);
  readonly selected = output<ChartSelection>();
  readonly retry = output<void>();

  protected readonly showTable = signal(false);
  protected readonly hasData = computed(() => !this.empty());

  protected onClick(event: ECElementEvent): void {
    const data = event.data as { key?: string } | null;
    const key = data?.key;
    if (key) {
      this.selected.emit({ key, series: event.seriesId ?? undefined });
    }
  }
}
