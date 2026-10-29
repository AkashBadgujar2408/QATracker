import type { EChartsCoreOption } from 'echarts/core';
import { ChartTokens } from './chart-theme';

/** One bar, slice or tile. `key` comes back on click; `color` is a CSS token name (`--qa-severity-high`). */
export interface ChartItem {
  readonly key: string;
  readonly label: string;
  readonly value: number;
  readonly color?: string;
}

/** A named series over shared categories (stacked bars, lines). */
export interface ChartSeries {
  readonly key: string;
  readonly name: string;
  /** CSS token name. */
  readonly color: string;
  readonly values: readonly (number | null)[];
  /** Dashed line (reference series such as an ideal burn-down). */
  readonly dashed?: boolean;
}

export interface ChartCategory {
  readonly key: string;
  readonly label: string;
}

export interface HeatmapData {
  readonly rows: readonly ChartCategory[];
  readonly columns: readonly ChartCategory[];
  /** value[row][column] */
  readonly values: readonly (readonly number[])[];
  /** CSS token name of the darkest step. */
  readonly color: string;
}

/** What the reader clicked: the category (or item) key and, for multi-series charts, the series key. */
export interface ChartSelection {
  readonly key: string;
  readonly series?: string;
}

/** Mark specs (dataviz): bars ≤ 24 px, 4 px rounded data end, 2 px surface gaps, 2 px lines, recessive axes. */
const BAR_MAX = 24;
const RADIUS = 4;

function base(t: ChartTokens, label: string): EChartsCoreOption {
  return {
    aria: { enabled: true, label: { description: label } },
    animationDuration: 300,
    textStyle: { fontFamily: t.fontFamily, color: t.muted, fontSize: 13 },
    tooltip: {
      confine: true,
      backgroundColor: t.surface,
      borderColor: t.border,
      borderWidth: 1,
      textStyle: { color: t.text, fontSize: 13, fontFamily: t.fontFamily },
      extraCssText: 'box-shadow: var(--qa-shadow-dialog); border-radius: 8px;',
    },
  };
}

function axisLine(t: ChartTokens) {
  return { lineStyle: { color: t.border } };
}

function splitLine(t: ChartTokens) {
  return { lineStyle: { color: t.border, width: 1, type: 'solid' } };
}

function legend(t: ChartTokens, names: readonly string[]) {
  return {
    data: [...names],
    top: 0,
    left: 0,
    icon: 'roundRect',
    itemWidth: 12,
    itemHeight: 8,
    textStyle: { color: t.text, fontSize: 13, fontFamily: t.fontFamily },
  };
}

/** "1.2K" from 1 000 up. */
export function compact(value: number): string {
  return Math.abs(value) >= 1000 ? `${Math.round(value / 100) / 10}K` : `${value}`;
}

/** Bars of single items. Horizontal bars put the label on the axis and the value at the tip. */
export function barOptions(
  t: ChartTokens,
  items: readonly ChartItem[],
  horizontal: boolean,
  label: string,
  format: (value: number) => string = String,
  max?: number,
): EChartsCoreOption {
  const labels = items.map((i) => i.label);
  const category = {
    type: 'category',
    data: horizontal ? [...labels].reverse() : labels,
    axisLine: axisLine(t),
    axisTick: { show: false },
    axisLabel: { color: t.text, fontSize: 13, hideOverlap: !horizontal },
  };
  const value = {
    type: 'value',
    max,
    minInterval: 1,
    splitLine: splitLine(t),
    axisLabel: { color: t.muted, formatter: compact },
  };
  const data = (horizontal ? [...items].reverse() : items).map((i) => ({
    value: i.value,
    key: i.key,
    itemStyle: { color: t.color(i.color ?? '--qa-primary') },
  }));
  return {
    ...base(t, label),
    grid: {
      left: 8,
      right: horizontal ? 40 : 8,
      top: horizontal ? 8 : 24,
      bottom: 8,
      containLabel: true,
    },
    tooltip: {
      ...(base(t, label)['tooltip'] as object),
      trigger: 'item',
      formatter: (p: { name: string; value: number }) =>
        `${p.name}: <strong>${format(p.value)}</strong>`,
    },
    xAxis: horizontal ? value : category,
    yAxis: horizontal ? category : value,
    series: [
      {
        type: 'bar',
        barMaxWidth: BAR_MAX,
        data,
        itemStyle: { borderRadius: horizontal ? [0, RADIUS, RADIUS, 0] : [RADIUS, RADIUS, 0, 0] },
        label: {
          // Selective labels: a value on every bar of a long series is noise (the tooltip and table carry it).
          show: items.length <= 12,
          formatter: (p: { value: number }) => format(p.value),
          position: horizontal ? 'right' : 'top',
          color: t.text,
          fontSize: 13,
          fontWeight: 600,
        },
        emphasis: { focus: 'self' },
      },
    ],
  };
}

/** Stacked bars (or 100 % stacked with `percent`), one series per stack part, 2 px surface gaps between parts. */
export function stackedBarOptions(
  t: ChartTokens,
  categories: readonly ChartCategory[],
  series: readonly ChartSeries[],
  horizontal: boolean,
  percent: boolean,
  label: string,
): EChartsCoreOption {
  const totals = categories.map((_, i) => series.reduce((sum, s) => sum + (s.values[i] ?? 0), 0));
  const labels = categories.map((c) => c.label);
  const category = {
    type: 'category',
    data: labels,
    inverse: horizontal,
    axisLine: axisLine(t),
    axisTick: { show: false },
    axisLabel: { color: t.text, fontSize: 13 },
  };
  const value = {
    type: 'value',
    max: percent ? 100 : undefined,
    minInterval: 1,
    splitLine: splitLine(t),
    axisLabel: { color: t.muted, formatter: percent ? '{value}%' : compact },
  };
  return {
    ...base(t, label),
    legend: legend(
      t,
      series.map((s) => s.name),
    ),
    grid: { left: 8, right: 16, top: 36, bottom: 8, containLabel: true },
    tooltip: {
      ...(base(t, label)['tooltip'] as object),
      trigger: 'axis',
      axisPointer: { type: 'shadow', shadowStyle: { color: 'rgba(127,127,127,0.08)' } },
    },
    xAxis: horizontal ? value : category,
    yAxis: horizontal ? category : value,
    series: series.map((s) => ({
      type: 'bar',
      name: s.name,
      id: s.key,
      stack: 'total',
      barMaxWidth: BAR_MAX,
      itemStyle: { color: t.color(s.color), borderColor: t.surface, borderWidth: 1 },
      emphasis: { focus: 'series' },
      data: s.values.map((v, i) => ({
        value: percent ? (totals[i] ? Math.round(((v ?? 0) / totals[i]) * 1000) / 10 : 0) : v,
        count: v,
        key: categories[i].key,
      })),
    })),
  };
}

/** Donut with the total in the middle; a 2 px surface gap between slices; labelled by the legend (with counts). */
export function donutOptions(
  t: ChartTokens,
  items: readonly ChartItem[],
  totalLabel: string,
  label: string,
): EChartsCoreOption {
  const total = items.reduce((sum, i) => sum + i.value, 0);
  const counts = new Map(items.map((i) => [i.label, i.value]));
  return {
    ...base(t, label),
    tooltip: {
      ...(base(t, label)['tooltip'] as object),
      trigger: 'item',
      formatter: '{b}: <strong>{c}</strong> ({d}%)',
    },
    legend: {
      ...legend(
        t,
        items.map((i) => i.label),
      ),
      orient: 'vertical',
      top: 'middle',
      left: '58%',
      itemGap: 10,
      formatter: (name: string) => `${name}  ${counts.get(name) ?? 0}`,
    },
    title: {
      text: compact(total),
      subtext: totalLabel,
      left: '27%',
      top: 'center',
      textAlign: 'center',
      itemGap: 2,
      textStyle: { color: t.text, fontSize: 26, fontWeight: 600, fontFamily: t.fontFamily },
      subtextStyle: { color: t.muted, fontSize: 13, fontFamily: t.fontFamily },
    },
    series: [
      {
        type: 'pie',
        radius: ['56%', '80%'],
        center: ['28%', '50%'],
        avoidLabelOverlap: true,
        label: { show: false },
        labelLine: { show: false },
        itemStyle: { borderColor: t.surface, borderWidth: 2, borderRadius: 3 },
        emphasis: { scale: true, scaleSize: 4 },
        data: items
          .filter((i) => i.value > 0)
          .map((i) => ({
            name: i.label,
            value: i.value,
            key: i.key,
            itemStyle: { color: t.color(i.color ?? '--qa-primary') },
          })),
      },
    ],
  };
}

/** Lines over categories (days, weeks) with a crosshair tooltip listing every series; `area` adds a 10 % wash. */
export function lineOptions(
  t: ChartTokens,
  categories: readonly ChartCategory[],
  series: readonly ChartSeries[],
  area: boolean,
  label: string,
): EChartsCoreOption {
  return {
    ...base(t, label),
    legend: legend(
      t,
      series.map((s) => s.name),
    ),
    grid: { left: 8, right: 16, top: 36, bottom: 8, containLabel: true },
    tooltip: {
      ...(base(t, label)['tooltip'] as object),
      trigger: 'axis',
      axisPointer: { type: 'line', lineStyle: { color: t.muted, width: 1 } },
    },
    xAxis: {
      type: 'category',
      boundaryGap: false,
      data: categories.map((c) => c.label),
      axisLine: axisLine(t),
      axisTick: { show: false },
      axisLabel: { color: t.muted, hideOverlap: true },
    },
    yAxis: {
      type: 'value',
      minInterval: 1,
      splitLine: splitLine(t),
      axisLabel: { color: t.muted, formatter: compact },
    },
    series: series.map((s) => ({
      type: 'line',
      name: s.name,
      id: s.key,
      data: s.values.map((v, i) => ({ value: v, key: categories[i].key })),
      smooth: false,
      showSymbol: false,
      symbol: 'circle',
      symbolSize: 8,
      connectNulls: false,
      lineStyle: {
        width: 2,
        color: t.color(s.color),
        type: s.dashed ? 'dashed' : 'solid',
        cap: 'round',
        join: 'round',
      },
      itemStyle: { color: t.color(s.color), borderColor: t.surface, borderWidth: 2 },
      areaStyle: area && !s.dashed ? { color: t.color(s.color), opacity: 0.1 } : undefined,
      emphasis: { focus: 'series' },
    })),
  };
}

/** Heat map: one hue from the surface to the token colour (sequential); counts shown in non-empty cells. */
export function heatmapOptions(
  t: ChartTokens,
  data: HeatmapData,
  label: string,
): EChartsCoreOption {
  const cells = data.rows.flatMap((_, r) =>
    data.columns.map((_, c) => [c, r, data.values[r]?.[c] ?? 0] as const),
  );
  const max = Math.max(1, ...cells.map((cell) => cell[2]));
  return {
    ...base(t, label),
    grid: { left: 8, right: 8, top: 8, bottom: 40, containLabel: true },
    tooltip: {
      ...(base(t, label)['tooltip'] as object),
      trigger: 'item',
      formatter: (p: { value: [number, number, number] }) =>
        `${data.rows[p.value[1]].label} · ${data.columns[p.value[0]].label}: <strong>${p.value[2]}</strong>`,
    },
    xAxis: {
      type: 'category',
      data: data.columns.map((c) => c.label),
      axisLine: axisLine(t),
      axisTick: { show: false },
      axisLabel: { color: t.text },
      splitArea: { show: false },
    },
    yAxis: {
      type: 'category',
      data: data.rows.map((r) => r.label),
      inverse: true,
      axisLine: axisLine(t),
      axisTick: { show: false },
      axisLabel: { color: t.text },
    },
    visualMap: {
      min: 0,
      max,
      show: true,
      orient: 'horizontal',
      left: 'center',
      bottom: 0,
      itemHeight: 120,
      itemWidth: 10,
      textStyle: { color: t.muted },
      inRange: { color: [t.surface, t.color(data.color)] },
    },
    series: [
      {
        type: 'heatmap',
        data: cells.map((cell) => ({
          value: [...cell],
          key: `${data.rows[cell[1]].key}|${data.columns[cell[0]].key}`,
        })),
        label: {
          show: true,
          color: t.text,
          fontWeight: 600,
          textBorderColor: t.surface,
          textBorderWidth: 3,
          formatter: (p: { value: number[] }) => (p.value[2] ? `${p.value[2]}` : ''),
        },
        itemStyle: { borderColor: t.surface, borderWidth: 2, borderRadius: 3 },
        emphasis: { itemStyle: { borderColor: t.text, borderWidth: 1 } },
      },
    ],
  };
}

/** Treemap of items (one level), labelled with name and count. */
export function treemapOptions(
  t: ChartTokens,
  items: readonly ChartItem[],
  label: string,
): EChartsCoreOption {
  return {
    ...base(t, label),
    tooltip: {
      ...(base(t, label)['tooltip'] as object),
      trigger: 'item',
      formatter: '{b}: <strong>{c}</strong>',
    },
    series: [
      {
        type: 'treemap',
        roam: false,
        nodeClick: false,
        breadcrumb: { show: false },
        width: '100%',
        height: '100%',
        top: 0,
        left: 0,
        itemStyle: { borderColor: t.surface, borderWidth: 2, gapWidth: 2, borderRadius: 4 },
        label: { color: '#ffffff', fontSize: 13, formatter: '{b}\n{c}' },
        data: items.map((i) => ({
          name: i.label,
          value: i.value,
          key: i.key,
          itemStyle: { color: t.color(i.color ?? '--qa-primary') },
        })),
      },
    ],
  };
}

/** Gauge for a rate 0–1 (shown as %). */
export function gaugeOptions(
  t: ChartTokens,
  rate: number,
  name: string,
  label: string,
): EChartsCoreOption {
  return {
    ...base(t, label),
    series: [
      {
        type: 'gauge',
        radius: '95%',
        center: ['50%', '58%'],
        min: 0,
        max: 100,
        startAngle: 210,
        endAngle: -30,
        progress: { show: true, width: 12, roundCap: true, itemStyle: { color: t.primary } },
        axisLine: { roundCap: true, lineStyle: { width: 12, color: [[1, t.border]] } },
        pointer: { show: false },
        axisTick: { show: false },
        splitLine: { show: false },
        axisLabel: { show: false },
        anchor: { show: false },
        title: { show: true, offsetCenter: [0, '32%'], color: t.muted, fontSize: 13 },
        detail: {
          valueAnimation: true,
          offsetCenter: [0, '-4%'],
          formatter: '{value}%',
          color: t.text,
          fontSize: 26,
          fontWeight: 600,
        },
        data: [{ value: Math.round(rate * 1000) / 10, name }],
      },
    ],
  };
}
