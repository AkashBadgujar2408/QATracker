import { Provider } from '@angular/core';
import { provideEchartsCore } from 'ngx-echarts';

/**
 * Add to the providers of a route that shows charts. ECharts itself is loaded on first use (its own lazy chunk),
 * so it never reaches the main bundle (ADR-005 budget).
 */
export function provideCharts(): Provider {
  return provideEchartsCore({
    echarts: () => import('./echarts-core').then((m) => m.echarts),
  });
}
