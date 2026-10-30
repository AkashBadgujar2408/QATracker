import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatTab, MatTabContent, MatTabGroup } from '@angular/material/tabs';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';
import { PageHeader } from '../../../shared/ui/page-header/page-header';
import { HolidaySettings } from './holiday-settings';
import { PhaseSettings } from './phase-settings';
import { SlaSettings } from './sla-settings';

export const SETTINGS_TABS = ['sla', 'holidays', 'phases'] as const;
export type SettingsTab = (typeof SETTINGS_TABS)[number];

/** Admin › Settings (plan 8.4, ADR-038): SLA hours, holidays and sprint phases. The open tab is kept in the URL. */
@Component({
  selector: 'app-settings-page',
  imports: [
    PageHeader,
    MatTabGroup,
    MatTab,
    MatTabContent,
    SlaSettings,
    HolidaySettings,
    PhaseSettings,
  ],
  template: `
    <app-page-header
      title="Settings"
      subtitle="How long developers have per severity, which days are holidays, and the sprint phases."
    />
    <div class="rounded-card border border-border bg-surface">
      <mat-tab-group
        mat-stretch-tabs="false"
        animationDuration="0"
        [selectedIndex]="index()"
        (selectedIndexChange)="select($event)"
      >
        <mat-tab label="SLA">
          <ng-template matTabContent>
            <div class="p-5"><app-sla-settings /></div>
          </ng-template>
        </mat-tab>
        <mat-tab label="Holidays">
          <ng-template matTabContent>
            <div class="p-5"><app-holiday-settings /></div>
          </ng-template>
        </mat-tab>
        <mat-tab label="Sprint phases">
          <ng-template matTabContent>
            <div class="p-5"><app-phase-settings /></div>
          </ng-template>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsPage {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly tab = toSignal(this.route.queryParamMap.pipe(map((p) => p.get('tab'))), {
    initialValue: null,
  });

  protected readonly index = computed(() =>
    Math.max(0, SETTINGS_TABS.indexOf(this.tab() as SettingsTab)),
  );

  protected select(index: number): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab: index === 0 ? null : SETTINGS_TABS[index] },
      replaceUrl: true,
    });
  }
}
