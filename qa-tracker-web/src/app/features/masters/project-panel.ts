import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MatError, MatFormField, MatHint, MatLabel, MatSuffix } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatTab, MatTabContent, MatTabGroup } from '@angular/material/tabs';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLock } from '@ng-icons/lucide';
import { problemMessage, problemOf } from '../../core/api/api';
import { ConfirmService } from '../../core/feedback/confirm.service';
import { ToastService } from '../../core/feedback/toast.service';
import { MastersApi, Project } from '../../shared/data/masters.api';
import { applyServerErrors, fieldError } from '../../shared/forms/form-errors';
import { PROJECT_KEY_PATTERN } from '../../shared/masters/master-dialog';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';
import { ProjectEnvironments } from './project-environments';
import { ProjectMembers } from './project-members';
import { ProjectModules } from './project-modules';

export const PROJECT_TABS = ['modules', 'environments', 'members'] as const;
export type ProjectTab = (typeof PROJECT_TABS)[number];

/** Detail panel of a project: its settings, then modules / environments / members. */
@Component({
  selector: 'app-project-panel',
  imports: [
    ReactiveFormsModule,
    MatButton,
    MatCheckbox,
    MatFormField,
    MatLabel,
    MatHint,
    MatError,
    MatSuffix,
    MatInput,
    MatTabGroup,
    MatTab,
    MatTabContent,
    NgIcon,
    EmptyState,
    SkeletonLines,
    ProjectModules,
    ProjectEnvironments,
    ProjectMembers,
  ],
  viewProviders: [provideIcons({ lucideLock })],
  template: `
    @if (project.isLoading() && !project.hasValue()) {
      <div class="qa-card p-6"><app-skeleton-lines [lines]="5" /></div>
    } @else if (project.error()) {
      <div class="qa-card">
        <app-empty-state kind="error" title="Could not load the project" [message]="errorMessage()">
          <button matButton="outlined" type="button" (click)="project.reload()">Try again</button>
        </app-empty-state>
      </div>
    } @else if (project.value(); as p) {
      <header class="mb-4 flex flex-wrap items-start gap-x-4 gap-y-2">
        <span
          class="rounded-control bg-primary px-2.5 py-1 text-lg leading-none font-bold tracking-wider text-primary-contrast"
          [class.opacity-50]="!p.isActive"
          >{{ p.key }}</span
        >
        <div class="min-w-0 flex-1">
          <h2 class="m-0 truncate text-lg leading-tight font-semibold">{{ p.name }}</h2>
          <p class="m-0 mt-0.5 text-sm text-muted">
            {{ p.clientName }}{{ p.isActive ? '' : ' – inactive project' }}
          </p>
        </div>
        @if (p.isActive) {
          <button matButton="outlined" type="button" (click)="setActive(p, false)">
            Deactivate
          </button>
        } @else {
          <button matButton="filled" type="button" (click)="setActive(p, true)">Activate</button>
        }
      </header>

      <form class="qa-card mb-4 p-5 pb-2" [formGroup]="form" (ngSubmit)="save(p)" novalidate>
        @if (error()) {
          <div
            role="alert"
            class="mb-3 rounded-control border border-severity-critical/40 bg-severity-critical/10 px-3 py-2 text-sm"
          >
            {{ error() }}
          </div>
        }
        <div class="grid gap-x-4 sm:grid-cols-[1fr_12rem]">
          <mat-form-field appearance="outline">
            <mat-label>Project name</mat-label>
            <input matInput formControlName="name" autocomplete="off" />
            <mat-error>{{ fieldError(form.controls.name, 'Project name') }}</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Key</mat-label>
            <input
              matInput
              formControlName="key"
              autocomplete="off"
              spellcheck="false"
              maxlength="8"
              class="font-semibold tracking-wide uppercase"
            />
            @if (p.hasIssues) {
              <ng-icon matSuffix name="lucideLock" size="16" class="mr-3 text-muted" />
            }
            @if (p.hasIssues) {
              <mat-hint>Locked – issues already use it.</mat-hint>
            }
            <mat-error>{{ fieldError(form.controls.key, 'Key') }}</mat-error>
          </mat-form-field>
        </div>
        <div class="flex flex-wrap items-center justify-between gap-3 pb-3">
          <mat-checkbox formControlName="requireLiveVerification">
            Fixes must also be verified on LIVE
          </mat-checkbox>
          @if (form.dirty) {
            <div class="flex gap-2">
              <button matButton type="button" (click)="reset(p)">Discard</button>
              <button matButton="filled" type="submit" [disabled]="saving()">Save changes</button>
            </div>
          }
        </div>
      </form>

      <section class="qa-card overflow-hidden">
        <mat-tab-group
          class="qa-tabs"
          [selectedIndex]="tabIndex()"
          (selectedIndexChange)="tab.set(tabs[$event])"
          animationDuration="0ms"
          mat-stretch-tabs="false"
        >
          <mat-tab [label]="'Modules (' + p.moduleCount + ')'">
            <ng-template matTabContent>
              <app-project-modules [project]="parent()!" (changed)="childChanged()" />
            </ng-template>
          </mat-tab>
          <mat-tab [label]="'Environments (' + p.environmentCount + ')'">
            <ng-template matTabContent>
              <app-project-environments [project]="parent()!" (changed)="childChanged()" />
            </ng-template>
          </mat-tab>
          <mat-tab [label]="'Members (' + p.memberCount + ')'">
            <ng-template matTabContent>
              <app-project-members [project]="parent()!" (changed)="childChanged()" />
            </ng-template>
          </mat-tab>
        </mat-tab-group>
      </section>
    }
  `,
  host: { class: 'block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectPanel {
  private readonly api = inject(MastersApi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);

  readonly projectId = input.required<number>();
  readonly tab = model<ProjectTab>('modules');
  /** Something in the project changed (name, key, counts, status). */
  readonly changed = output<Project>();

  protected readonly tabs = PROJECT_TABS;
  protected readonly tabIndex = computed(() => PROJECT_TABS.indexOf(this.tab()));
  protected readonly fieldError = fieldError;
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly project = rxResource({
    params: () => this.projectId(),
    stream: ({ params }) => this.api.project(params),
  });
  protected readonly parent = computed(() => {
    const project = this.project.value();
    return project ? { id: project.id, name: project.name } : null;
  });
  protected readonly errorMessage = computed(() => problemMessage(this.project.error()));

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    key: ['', [Validators.required, Validators.pattern(PROJECT_KEY_PATTERN)]],
    requireLiveVerification: [false],
  });

  constructor() {
    // Fill the form when a project loads; a reload (e.g. counts changed) keeps unsaved edits.
    let shownId: number | null = null;
    effect(() => {
      const project = this.project.value();
      if (project && (project.id !== shownId || !untracked(() => this.form.dirty))) {
        shownId = project.id;
        this.reset(project);
      }
    });
    this.form.controls.key.valueChanges.subscribe((key) => {
      const upper = key.toUpperCase().replace(/\s/g, '');
      if (upper !== key) {
        this.form.controls.key.setValue(upper, { emitEvent: false });
      }
    });
  }

  protected reset(project: Project): void {
    this.error.set(null);
    this.form.reset({
      name: project.name,
      key: project.key,
      requireLiveVerification: project.requireLiveVerification,
    });
    if (project.hasIssues) {
      this.form.controls.key.disable();
    } else {
      this.form.controls.key.enable();
    }
  }

  protected save(project: Project): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.saving.set(true);
    this.error.set(null);
    this.api
      .updateProject(project.id, {
        name: value.name.trim(),
        key: value.key,
        requireLiveVerification: value.requireLiveVerification,
        rowVersion: project.rowVersion,
      })
      .subscribe({
        next: (saved) => {
          this.saving.set(false);
          this.project.set(saved);
          this.reset(saved);
          this.toast.success(`Project ${saved.name} saved.`);
          this.changed.emit(saved);
        },
        error: (error: unknown) => {
          this.saving.set(false);
          const problem = problemOf(error);
          const unmatched = applyServerErrors(this.form, problem);
          if (problem?.code === 'project_name_taken') {
            this.form.controls.name.setErrors({ server: problem.detail });
          } else if (problem?.code === 'project_key_taken' || problem?.code === 'key_locked') {
            this.form.controls.key.setErrors({ server: problem.detail });
          } else if (!problem?.errors || unmatched.length) {
            this.error.set(unmatched[0] ?? problemMessage(error));
          }
        },
      });
  }

  protected async setActive(project: Project, isActive: boolean): Promise<void> {
    if (!isActive) {
      const ok = await this.confirm.ask({
        title: `Deactivate ${project.name}?`,
        message:
          'It disappears from the pickers on the Testing screen. Its sprints and issues stay as they are.',
        confirmLabel: 'Deactivate',
        danger: true,
      });
      if (!ok) {
        return;
      }
    }
    this.api.setProjectActive(project.id, isActive).subscribe({
      next: (saved) => {
        this.project.set(saved);
        this.toast.success(`Project ${saved.name} ${isActive ? 'activated' : 'deactivated'}.`);
        this.changed.emit(saved);
      },
      error: (error: unknown) => this.toast.error(problemMessage(error)),
    });
  }

  protected childChanged(): void {
    this.project.reload();
    const project = this.project.value();
    if (project) {
      this.changed.emit(project);
    }
  }
}
