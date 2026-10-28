import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCirclePause,
  lucideCirclePlay,
  lucideEllipsisVertical,
  lucidePencil,
  lucidePlus,
} from '@ng-icons/lucide';
import { problemMessage } from '../../core/api/api';
import { ToastService } from '../../core/feedback/toast.service';
import { MastersApi, Module } from '../../shared/data/masters.api';
import { MasterDialogs } from '../../shared/masters/master-dialogs.service';
import { MasterParent } from '../../shared/masters/master-dialog';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';

/** Modules tab of a project: functional areas issues are filed under. */
@Component({
  selector: 'app-project-modules',
  imports: [MatButton, MatIconButton, MatMenuModule, NgIcon, EmptyState, SkeletonLines],
  viewProviders: [
    provideIcons({
      lucideCirclePause,
      lucideCirclePlay,
      lucideEllipsisVertical,
      lucidePencil,
      lucidePlus,
    }),
  ],
  template: `
    @if (modules.isLoading() && !modules.hasValue()) {
      <app-skeleton-lines [lines]="4" class="block p-4" />
    } @else if (modules.error()) {
      <app-empty-state kind="error" title="Could not load modules" [message]="errorMessage()">
        <button matButton="outlined" type="button" (click)="modules.reload()">Try again</button>
      </app-empty-state>
    } @else if (modules.value()?.length === 0) {
      <app-empty-state
        title="No modules yet"
        message="Modules are the functional areas of the project, like Sales or Inventory. Every issue belongs to one."
      >
        <button matButton="filled" type="button" (click)="add()">Add module</button>
      </app-empty-state>
    } @else {
      <div class="flex items-center justify-between gap-3 px-4 py-3">
        <span class="text-sm text-muted tabular-nums">{{ summary() }}</span>
        <button matButton="outlined" type="button" (click)="add()">
          <ng-icon name="lucidePlus" size="16" class="mr-1" />Add module
        </button>
      </div>
      <ul class="m-0 list-none border-t border-border p-0">
        @for (module of modules.value(); track module.id) {
          <li
            class="flex min-h-12 items-center gap-3 border-b border-border px-4 last:border-b-0"
            [class.text-muted]="!module.isActive"
          >
            <span class="min-w-0 flex-1 truncate">{{ module.name }}</span>
            @if (!module.isActive) {
              <span class="rounded-pill bg-surface-muted px-2 py-0.5 text-xs">Inactive</span>
            }
            <button
              matIconButton
              type="button"
              [matMenuTriggerFor]="menu"
              [matMenuTriggerData]="{ module }"
              [attr.aria-label]="'Actions for ' + module.name"
            >
              <ng-icon name="lucideEllipsisVertical" size="18" />
            </button>
          </li>
        }
      </ul>
    }

    <mat-menu #menu="matMenu" xPosition="before">
      <ng-template matMenuContent let-module="module">
        <button mat-menu-item (click)="rename(module)">
          <ng-icon name="lucidePencil" size="16" class="mr-3" /><span>Rename</span>
        </button>
        <button mat-menu-item (click)="setActive(module, !module.isActive)">
          <ng-icon
            [name]="module.isActive ? 'lucideCirclePause' : 'lucideCirclePlay'"
            size="16"
            class="mr-3"
          /><span>{{ module.isActive ? 'Deactivate' : 'Activate' }}</span>
        </button>
      </ng-template>
    </mat-menu>
  `,
  host: { class: 'block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectModules {
  private readonly api = inject(MastersApi);
  private readonly dialogs = inject(MasterDialogs);
  private readonly toast = inject(ToastService);

  readonly project = input.required<MasterParent>();
  readonly changed = output<void>();

  protected readonly modules = rxResource({
    params: () => this.project().id,
    stream: ({ params }) => this.api.modules(params, true),
  });

  protected readonly summary = computed(() => {
    const modules = this.modules.value() ?? [];
    const active = modules.filter((m) => m.isActive).length;
    const inactive = modules.length - active;
    return `${active} active${inactive ? `, ${inactive} inactive` : ''}`;
  });
  protected readonly errorMessage = computed(() => problemMessage(this.modules.error()));

  protected async add(): Promise<void> {
    const result = await this.dialogs.open({ kind: 'Module', parent: this.project() });
    if (result?.isNew) {
      this.toast.success(`Module ${result.name} added.`);
      this.refresh();
    }
  }

  protected async rename(module: Module): Promise<void> {
    const result = await this.dialogs.open({ kind: 'Module', parent: this.project(), module });
    if (result) {
      this.toast.success(`Module ${result.name} saved.`);
      this.refresh();
    }
  }

  protected setActive(module: Module, isActive: boolean): void {
    this.api.setModuleActive(module.id, isActive).subscribe({
      next: () => {
        this.toast.success(`Module ${module.name} ${isActive ? 'activated' : 'deactivated'}.`);
        this.refresh();
      },
      error: (error: unknown) => this.toast.error(problemMessage(error)),
    });
  }

  private refresh(): void {
    this.modules.reload();
    this.changed.emit();
  }
}
