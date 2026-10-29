import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  inject,
  input,
} from '@angular/core';
import { Chart, ChartInputs, ChartTable } from './chart';
import {
  ChartCategory,
  ChartItem,
  ChartSeries,
  HeatmapData,
  barOptions,
  donutOptions,
  gaugeOptions,
  heatmapOptions,
  lineOptions,
  stackedBarOptions,
  treemapOptions,
} from './chart-options';
import { ChartTheme } from './chart-theme';

const FRAME = `
  <app-chart
    [title]="title()"
    [subtitle]="subtitle()"
    [options]="options()"
    [table]="table()"
    [loading]="loading()"
    [error]="error()"
    [empty]="empty()"
    [emptyMessage]="emptyMessage()"
    [height]="height()"
    (selected)="selected.emit($event)"
    (retry)="retry.emit()"
  />
`;

const HOST = { class: 'block min-w-0' };

function itemTable(
  items: readonly ChartItem[],
  valueLabel: string,
  format: (value: number) => string = String,
): ChartTable {
  return { columns: ['', valueLabel], rows: items.map((i) => [i.label, format(i.value)]) };
}

function seriesTable(
  categories: readonly ChartCategory[],
  series: readonly ChartSeries[],
): ChartTable {
  return {
    columns: ['', ...series.map((s) => s.name)],
    rows: categories.map((c, i) => [c.label, ...series.map((s) => s.values[i] ?? '–')]),
  };
}

/** Bars of single values (plan 9.6 `app-chart-bar`). Horizontal by default – labels read better. */
@Component({
  selector: 'app-chart-bar',
  imports: [Chart],
  template: FRAME,
  host: HOST,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChartBar extends ChartInputs {
  private readonly theme = inject(ChartTheme);
  readonly items = input.required<readonly ChartItem[]>();
  readonly horizontal = input(true, { transform: booleanAttribute });
  readonly valueLabel = input('Issues');
  /** How values read on the bars, in the tooltip and the table (e.g. `(v) => v + ' h'`). */
  readonly format = input<(value: number) => string>(String);
  /** Fixed end of the value axis (100 for percentages). */
  readonly max = input<number>();

  protected readonly options = computed(() =>
    barOptions(
      this.theme.tokens(),
      this.items(),
      this.horizontal(),
      this.title(),
      this.format(),
      this.max(),
    ),
  );
  protected readonly table = computed(() =>
    itemTable(this.items(), this.valueLabel(), this.format()),
  );
  /** Counts: all zero means nothing to show. Metrics (hours, rates): 0 is a value – only no items is empty. */
  readonly zeroIsEmpty = input(true, { transform: booleanAttribute });

  protected readonly empty = computed(() =>
    this.zeroIsEmpty() ? this.items().every((i) => !i.value) : this.items().length === 0,
  );
}

/** Stacked (or 100 % stacked) bars. */
@Component({
  selector: 'app-chart-stacked-bar',
  imports: [Chart],
  template: FRAME,
  host: HOST,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChartStackedBar extends ChartInputs {
  private readonly theme = inject(ChartTheme);
  readonly categories = input.required<readonly ChartCategory[]>();
  readonly series = input.required<readonly ChartSeries[]>();
  readonly horizontal = input(true, { transform: booleanAttribute });
  readonly percent = input(false, { transform: booleanAttribute });

  protected readonly options = computed(() =>
    stackedBarOptions(
      this.theme.tokens(),
      this.categories(),
      this.series(),
      this.horizontal(),
      this.percent(),
      this.title(),
    ),
  );
  protected readonly table = computed(() => seriesTable(this.categories(), this.series()));
  protected readonly empty = computed(() => this.series().every((s) => s.values.every((v) => !v)));
}

/** Donut with the total in the middle. */
@Component({
  selector: 'app-chart-donut',
  imports: [Chart],
  template: FRAME,
  host: HOST,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChartDonut extends ChartInputs {
  private readonly theme = inject(ChartTheme);
  readonly items = input.required<readonly ChartItem[]>();
  readonly totalLabel = input('issues');

  protected readonly options = computed(() =>
    donutOptions(this.theme.tokens(), this.items(), this.totalLabel(), this.title()),
  );
  protected readonly table = computed(() => itemTable(this.items(), 'Issues'));
  protected readonly empty = computed(() => this.items().every((i) => !i.value));
}

/** Lines over days or weeks (`area` adds a light wash). */
@Component({
  selector: 'app-chart-line',
  imports: [Chart],
  template: FRAME,
  host: HOST,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChartLine extends ChartInputs {
  private readonly theme = inject(ChartTheme);
  readonly categories = input.required<readonly ChartCategory[]>();
  readonly series = input.required<readonly ChartSeries[]>();
  readonly area = input(false, { transform: booleanAttribute });
  /** Stacked areas – the series are parts of one total. */
  readonly stacked = input(false, { transform: booleanAttribute });

  protected readonly options = computed(() =>
    lineOptions(
      this.theme.tokens(),
      this.categories(),
      this.series(),
      this.area(),
      this.title(),
      this.stacked(),
    ),
  );
  protected readonly table = computed(() => seriesTable(this.categories(), this.series()));
  protected readonly empty = computed(() => this.series().every((s) => s.values.every((v) => !v)));
}

/** Heat map (rows × columns). */
@Component({
  selector: 'app-chart-heatmap',
  imports: [Chart],
  template: FRAME,
  host: HOST,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChartHeatmap extends ChartInputs {
  private readonly theme = inject(ChartTheme);
  readonly data = input.required<HeatmapData>();

  protected readonly options = computed(() =>
    heatmapOptions(this.theme.tokens(), this.data(), this.title()),
  );
  protected readonly table = computed(() => {
    const d = this.data();
    return {
      columns: ['', ...d.columns.map((c) => c.label)],
      rows: d.rows.map((r, i) => [r.label, ...d.columns.map((_, j) => d.values[i]?.[j] ?? 0)]),
    };
  });
  protected readonly empty = computed(() =>
    this.data().values.every((row) => row.every((v) => !v)),
  );
}

/** Treemap of items. */
@Component({
  selector: 'app-chart-treemap',
  imports: [Chart],
  template: FRAME,
  host: HOST,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChartTreemap extends ChartInputs {
  private readonly theme = inject(ChartTheme);
  readonly items = input.required<readonly ChartItem[]>();

  protected readonly options = computed(() =>
    treemapOptions(this.theme.tokens(), this.items(), this.title()),
  );
  protected readonly table = computed(() => itemTable(this.items(), 'Issues'));
  protected readonly empty = computed(() => this.items().every((i) => !i.value));
}

/** Gauge for a rate (0–1). */
@Component({
  selector: 'app-chart-gauge',
  imports: [Chart],
  template: FRAME,
  host: HOST,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChartGauge extends ChartInputs {
  private readonly theme = inject(ChartTheme);
  readonly rate = input.required<number>();
  readonly name = input('');
  /** How many items the rate is based on – 0 shows the empty state. */
  readonly count = input(1);

  protected readonly options = computed(() =>
    gaugeOptions(this.theme.tokens(), this.rate(), this.name(), this.title()),
  );
  protected readonly table = computed<ChartTable>(() => ({
    columns: ['', 'Value'],
    rows: [[this.name() || this.title(), `${Math.round(this.rate() * 1000) / 10}%`]],
  }));
  protected readonly empty = computed(() => this.count() === 0);
}
