import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCirclePause,
  lucideCirclePlay,
  lucideEllipsisVertical,
  lucideExternalLink,
  lucidePencil,
  lucidePlus,
} from '@ng-icons/lucide';
import { problemMessage } from '../../core/api/api';
import { ToastService } from '../../core/feedback/toast.service';
import { MastersApi, ProjectEnvironment } from '../../shared/data/masters.api';
import { MasterParent } from '../../shared/masters/master-dialog';
import { MasterDialogs } from '../../shared/masters/master-dialogs.service';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';

/** Environments tab of a project: the UAT / LIVE URLs fixes are deployed to and retested on. */
@Component({
  selector: 'app-project-environments',
  imports: [MatButton, MatIconButton, MatMenuModule, NgIcon, EmptyState, SkeletonLines],
  viewProviders: [
    provideIcons({
      lucideCirclePause,
      lucideCirclePlay,
      lucideEllipsisVertical,
      lucideExternalLink,
      lucidePencil,
      lucidePlus,
    }),
  ],
  template: `
    @if (environments.isLoading() && !environments.hasValue()) {
      <app-skeleton-lines [lines]="3" class="block p-4" />
    } @else if (environments.error()) {
      <app-empty-state kind="error" title="Could not load environments" [message]="errorMessage()">
        <button matButton="outlined" type="button" (click)="environments.reload()">
          Try again
        </button>
      </app-empty-state>
    } @else if (environments.value()?.length === 0) {
      <app-empty-state
        title="No environments yet"
        message="Add the UAT and LIVE addresses where this project runs, so retests know where to look."
      >
        <button matButton="filled" type="button" (click)="add()">Add environment</button>
      </app-empty-state>
    } @else {
      <div class="flex items-center justify-between gap-3 px-4 py-3">
        <span class="text-sm text-muted tabular-nums">{{ summary() }}</span>
        <button matButton="outlined" type="button" (click)="add()">
          <ng-icon name="lucidePlus" size="16" class="mr-1" />Add environment
        </button>
      </div>
      <ul class="m-0 list-none border-t border-border p-0">
        @for (environment of environments.value(); track environment.id) {
          <li
            class="flex min-h-14 items-center gap-3 border-b border-border px-4 py-2 last:border-b-0"
            [class.text-muted]="!environment.isActive"
          >
            <span
              class="w-14 shrink-0 rounded-control px-2 py-0.5 text-center text-xs font-semibold"
              [class]="typeClass(environment)"
              >{{ environment.type }}</span
            >
            <div class="min-w-0 flex-1">
              <div class="truncate font-medium">{{ environment.name }}</div>
              <a
                class="inline-flex max-w-full items-center gap-1 truncate text-sm text-muted hover:text-primary"
                [href]="environment.url"
                target="_blank"
                rel="noopener noreferrer"
                >{{ environment.url }}<ng-icon name="lucideExternalLink" size="12" class="shrink-0"
              /></a>
            </div>
            @if (!environment.isActive) {
              <span class="rounded-pill bg-surface-muted px-2 py-0.5 text-xs">Inactive</span>
            }
            <button
              matIconButton
              type="button"
              [matMenuTriggerFor]="menu"
              [matMenuTriggerData]="{ environment }"
              [attr.aria-label]="'Actions for ' + environment.name"
            >
              <ng-icon name="lucideEllipsisVertical" size="18" />
            </button>
          </li>
        }
      </ul>
    }

    <mat-menu #menu="matMenu" xPosition="before">
      <ng-template matMenuContent let-environment="environment">
        <button mat-menu-item (click)="edit(environment)">
          <ng-icon name="lucidePencil" size="16" class="mr-3" /><span>Edit</span>
        </button>
        <button mat-menu-item (click)="setActive(environment, !environment.isActive)">
          <ng-icon
            [name]="environment.isActive ? 'lucideCirclePause' : 'lucideCirclePlay'"
            size="16"
            class="mr-3"
          /><span>{{ environment.isActive ? 'Deactivate' : 'Activate' }}</span>
        </button>
      </ng-template>
    </mat-menu>
  `,
  host: { class: 'block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectEnvironments {
  private readonly api = inject(MastersApi);
  private readonly dialogs = inject(MasterDialogs);
  private readonly toast = inject(ToastService);

  readonly project = input.required<MasterParent>();
  readonly changed = output<void>();

  protected readonly environments = rxResource({
    params: () => this.project().id,
    stream: ({ params }) => this.api.environments(params, true),
  });

  protected readonly summary = computed(() => {
    const environments = this.environments.value() ?? [];
    const active = environments.filter((e) => e.isActive).length;
    const inactive = environments.length - active;
    return `${active} active${inactive ? `, ${inactive} inactive` : ''}`;
  });
  protected readonly errorMessage = computed(() => problemMessage(this.environments.error()));

  protected typeClass(environment: ProjectEnvironment): string {
    switch (environment.type) {
      case 'LIVE':
        return 'bg-status-verified/15 text-status-verified';
      case 'UAT':
        return 'bg-primary/10 text-primary';
      default:
        return 'bg-surface-muted text-muted';
    }
  }

  protected async add(): Promise<void> {
    const result = await this.dialogs.open({ kind: 'Environment', parent: this.project() });
    if (result?.isNew) {
      this.toast.success(`Environment ${result.name} added.`);
      this.refresh();
    }
  }

  protected async edit(environment: ProjectEnvironment): Promise<void> {
    const result = await this.dialogs.open({
      kind: 'Environment',
      parent: this.project(),
      environment,
    });
    if (result) {
      this.toast.success(`Environment ${result.name} saved.`);
      this.refresh();
    }
  }

  protected setActive(environment: ProjectEnvironment, isActive: boolean): void {
    this.api.setEnvironmentActive(environment.id, isActive).subscribe({
      next: () => {
        this.toast.success(
          `Environment ${environment.name} ${isActive ? 'activated' : 'deactivated'}.`,
        );
        this.refresh();
      },
      error: (error: unknown) => this.toast.error(problemMessage(error)),
    });
  }

  private refresh(): void {
    this.environments.reload();
    this.changed.emit();
  }
}
