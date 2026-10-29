import { TestBed } from '@angular/core/testing';
import { convertToParamMap } from '@angular/router';
import { ChartTokens } from './chart-theme';
import { ChartBar } from './charts';
import { provideCharts } from './echarts';
import {
  barOptions,
  compact,
  donutOptions,
  heatmapOptions,
  lineOptions,
  stackedBarOptions,
} from './chart-options';
import {
  IstDateAdapter,
  analyticsQuery,
  dashboardFiltersFromParams,
  dashboardFiltersToParams,
  istToday,
} from './dashboard-filters';
import { trendText } from './kpi-card';

const tokens: ChartTokens = {
  text: '#0f172a',
  muted: '#64748b',
  surface: '#ffffff',
  border: '#e2e8f0',
  primary: '#4f46e5',
  fontFamily: 'Inter',
  color: (token) => `color(${token})`,
};

/** Reads a nested option value: at(o, 'series.0.barMaxWidth'). */
function at(o: unknown, path: string): unknown {
  return path
    .split('.')
    .reduce<unknown>((v, k) => (v as Record<string, unknown> | undefined)?.[k], o);
}

describe('chart options', () => {
  it('draws thin rounded bars in token colours with the key for click-through', () => {
    const o = barOptions(
      tokens,
      [
        { key: 'Critical', label: 'Critical', value: 2, color: '--qa-severity-critical' },
        { key: 'High', label: 'High', value: 5, color: '--qa-severity-high' },
      ],
      true,
      'Open by severity',
    );
    const series = at(o, 'series.0');
    expect(at(series, 'barMaxWidth')).toBe(24);
    expect(at(series, 'itemStyle.borderRadius')).toEqual([0, 4, 4, 0]);
    // Horizontal bars list the first item at the top (category axis bottom-up).
    expect(at(o, 'yAxis.data')).toEqual(['High', 'Critical']);
    expect(at(series, 'data.1')).toEqual({
      value: 2,
      key: 'Critical',
      itemStyle: { color: 'color(--qa-severity-critical)' },
    });
    expect(at(o, 'tooltip.confine')).toBe(true);
    expect(at(o, 'aria.enabled')).toBe(true);
  });

  it('stacks series and converts to percentages for 100 % bars', () => {
    const o = stackedBarOptions(
      tokens,
      [{ key: '1', label: 'Emily' }],
      [
        { key: 'a', name: 'Assigned', color: '--x', values: [3] },
        { key: 'b', name: 'In Progress', color: '--y', values: [1] },
      ],
      true,
      true,
      'Workload',
    );
    expect((at(o, 'series') as unknown[]).map((s) => at(s, 'data.0.value'))).toEqual([75, 25]);
    expect((at(o, 'series') as unknown[]).map((s) => at(s, 'id'))).toEqual(['a', 'b']);
    expect(at(o, 'legend.data')).toEqual(['Assigned', 'In Progress']);
  });

  it('shows the donut total, hides empty slices and lists counts in the legend', () => {
    const o = donutOptions(
      tokens,
      [
        { key: 'Open', label: 'Open', value: 6 },
        { key: 'Closed', label: 'Closed', value: 0 },
      ],
      'issues',
      'By status',
    );
    expect(at(o, 'title.text')).toBe('6');
    expect(at(o, 'series.0.data')).toHaveLength(1);
    expect((at(o, 'legend.formatter') as (name: string) => string)('Open')).toBe('Open  6');
    expect(at(o, 'series.0.itemStyle.borderWidth')).toBe(2);
  });

  it('stacks line series as filled areas only when asked', () => {
    const categories = [
      { key: 'a', label: '19/Oct' },
      { key: 'b', label: '26/Oct' },
    ];
    const series = [{ key: 'High', name: 'High', color: '--qa-severity-high', values: [3, 1] }];
    const stacked = lineOptions(tokens, categories, series, false, 'Trend', true);
    const plain = lineOptions(tokens, categories, series, false, 'Trend');
    expect(at(stacked, 'series.0.stack')).toBe('total');
    expect(at(stacked, 'series.0.areaStyle.opacity') as number).toBeGreaterThan(0.5);
    expect(at(plain, 'series.0.stack')).toBeUndefined();
    expect(at(plain, 'series.0.areaStyle')).toBeUndefined();
  });

  it('draws 2 px lines with a crosshair and dashed reference series without area', () => {
    const o = lineOptions(
      tokens,
      [
        { key: 'a', label: '27/Oct' },
        { key: 'b', label: '28/Oct' },
      ],
      [
        { key: 'open', name: 'Open', color: '--qa-primary', values: [3, null] },
        { key: 'ideal', name: 'Ideal', color: '--m', values: [3, 0], dashed: true },
      ],
      true,
      'Burn-down',
    );
    expect(at(o, 'tooltip.trigger')).toBe('axis');
    expect(at(o, 'series.0.lineStyle.width')).toBe(2);
    expect(at(o, 'series.0.areaStyle.opacity')).toBe(0.1);
    expect(at(o, 'series.1.lineStyle.type')).toBe('dashed');
    expect(at(o, 'series.1.areaStyle')).toBeUndefined();
  });

  it('maps heat map cells to [column, row, value] with a row|column key', () => {
    const o = heatmapOptions(
      tokens,
      {
        rows: [{ key: 'm1', label: 'Sales' }],
        columns: [
          { key: 'High', label: 'High' },
          { key: 'Low', label: 'Low' },
        ],
        values: [[4, 0]],
        color: '--qa-primary',
      },
      'Module × severity',
    );
    expect(at(o, 'series.0.data')).toEqual([
      { value: [0, 0, 4], key: 'm1|High' },
      { value: [1, 0, 0], key: 'm1|Low' },
    ]);
    expect(at(o, 'visualMap.max')).toBe(4);
  });

  it('formats axis numbers compactly from 1 000 up', () => {
    expect([0, 999, 1000, 1250, 12_400].map(compact)).toEqual(['0', '999', '1K', '1.3K', '12.4K']);
  });
});

describe('dashboard filters', () => {
  it('reads presets and custom ranges from the URL and writes back short URLs', () => {
    const preset = dashboardFiltersFromParams(convertToParamMap({ client: '2', period: '7' }));
    expect(preset).toEqual({
      clientId: 2,
      projectId: null,
      sprintId: null,
      period: '7',
      from: null,
      to: null,
    });
    const custom = dashboardFiltersFromParams(
      convertToParamMap({ from: '2026-10-01', to: '2026-10-07' }),
    );
    expect(custom.period).toBe('custom');
    expect(
      dashboardFiltersFromParams(convertToParamMap({ from: '2026-10-09', to: '2026-10-01' }))
        .period,
    ).toBe('30');
    expect(dashboardFiltersToParams({ ...preset, period: '30' })).toMatchObject({ period: null });
    expect(dashboardFiltersToParams(custom)).toMatchObject({
      from: '2026-10-01',
      to: '2026-10-07',
    });
  });

  it('turns periods into IST days for the API', () => {
    // 29/Oct/2026 20:00 UTC is already 30/Oct in IST.
    const now = new Date('2026-10-29T20:00:00Z');
    expect(istToday(now)).toBe('2026-10-30');
    const base = dashboardFiltersFromParams(convertToParamMap({}));
    expect(analyticsQuery(base, now)).toMatchObject({ from: '2026-10-01', to: '2026-10-30' });
    expect(analyticsQuery({ ...base, period: '7' }, now)).toMatchObject({ from: '2026-10-24' });
    expect(analyticsQuery({ ...base, period: 'month' }, now)).toMatchObject({ from: '2026-10-01' });
  });

  it('formats and parses picker dates as dd/MMM/yyyy', () => {
    const adapter = TestBed.runInInjectionContext(() => new IstDateAdapter());
    expect(adapter.format(new Date(2026, 9, 27))).toBe('27/Oct/2026');
    expect(adapter.parse('27/oct/2026')?.getDate()).toBe(27);
    expect(adapter.parse('27/10/2026')?.getMonth()).toBe(9);
    expect(adapter.isValid(adapter.parse('31/Feb/2026')!)).toBe(false);
    expect(adapter.parse('')).toBeNull();
  });

  it('describes a KPI trend in words', () => {
    expect(trendText(9, 6, 'since last week')).toBe('+3 since last week');
    expect(trendText(4, 6, 'since last week')).toBe('−2 since last week');
    expect(trendText(6, 6, 'since last week')).toBe('No change since last week');
  });
});

describe('ChartBar empty state', () => {
  async function render(zeroIsEmpty: boolean): Promise<string> {
    TestBed.configureTestingModule({ providers: [provideCharts()] });
    const fixture = TestBed.createComponent(ChartBar);
    fixture.componentRef.setInput('title', 'Average fix time');
    fixture.componentRef.setInput('items', [{ key: '5', label: 'Emily', value: 0 }]);
    fixture.componentRef.setInput('zeroIsEmpty', zeroIsEmpty);
    fixture.componentRef.setInput('emptyMessage', 'Nothing was fixed.');
    await fixture.whenStable();
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  beforeAll(() => {
    globalThis.ResizeObserver ??= class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    };
  });

  it('treats all-zero counts as empty but keeps a metric of 0', async () => {
    expect(await render(true)).toContain('Nothing was fixed.');
    TestBed.resetTestingModule();
    expect(await render(false)).not.toContain('Nothing was fixed.');
  });
});
