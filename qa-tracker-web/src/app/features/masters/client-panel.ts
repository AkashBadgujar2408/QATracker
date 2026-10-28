import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatError, MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronRight, lucidePlus } from '@ng-icons/lucide';
import { problemMessage, problemOf } from '../../core/api/api';
import { ConfirmService } from '../../core/feedback/confirm.service';
import { ToastService } from '../../core/feedback/toast.service';
import { Client, MastersApi } from '../../shared/data/masters.api';
import { applyServerErrors, fieldError } from '../../shared/forms/form-errors';
import { PluralPipe } from '../../shared/pipes/plural.pipe';
import { MasterDialogs } from '../../shared/masters/master-dialogs.service';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';

/** Detail panel of a client: contact details and its projects. */
@Component({
  selector: 'app-client-panel',
  imports: [
    ReactiveFormsModule,
    MatButton,
    MatFormField,
    MatLabel,
    MatError,
    MatInput,
    NgIcon,
    EmptyState,
    SkeletonLines,
    PluralPipe,
  ],
  viewProviders: [provideIcons({ lucideChevronRight, lucidePlus })],
  template: `
    @if (client.isLoading() && !client.hasValue()) {
      <div class="qa-card p-6"><app-skeleton-lines [lines]="5" /></div>
    } @else if (client.error()) {
      <div class="qa-card">
        <app-empty-state kind="error" title="Could not load the client" [message]="errorMessage()">
          <button matButton="outlined" type="button" (click)="client.reload()">Try again</button>
        </app-empty-state>
      </div>
    } @else if (client.value(); as c) {
      <header class="mb-4 flex flex-wrap items-start gap-x-4 gap-y-2">
        <div class="min-w-0 flex-1">
          <h2 class="m-0 truncate text-lg leading-tight font-semibold">{{ c.name }}</h2>
          <p class="m-0 mt-0.5 text-sm text-muted">Client{{ c.isActive ? '' : ' – inactive' }}</p>
        </div>
        @if (c.isActive) {
          <button matButton="outlined" type="button" (click)="setActive(c, false)">
            Deactivate
          </button>
        } @else {
          <button matButton="filled" type="button" (click)="setActive(c, true)">Activate</button>
        }
      </header>

      <form class="qa-card mb-4 p-5 pb-2" [formGroup]="form" (ngSubmit)="save(c)" novalidate>
        @if (error()) {
          <div
            role="alert"
            class="mb-3 rounded-control border border-severity-critical/40 bg-severity-critical/10 px-3 py-2 text-sm"
          >
            {{ error() }}
          </div>
        }
        <div class="grid gap-x-4 sm:grid-cols-2">
          <mat-form-field appearance="outline" class="sm:col-span-2">
            <mat-label>Client name</mat-label>
            <input matInput formControlName="name" autocomplete="off" />
            <mat-error>{{ fieldError(form.controls.name, 'Client name') }}</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Contact person</mat-label>
            <input matInput formControlName="contactPerson" autocomplete="off" />
            <mat-error>{{ fieldError(form.controls.contactPerson, 'Contact person') }}</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Phone</mat-label>
            <input matInput type="tel" formControlName="phone" autocomplete="off" />
            <mat-error>{{ fieldError(form.controls.phone, 'Phone') }}</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline" class="sm:col-span-2">
            <mat-label>E-mail</mat-label>
            <input matInput type="email" formControlName="email" autocomplete="off" />
            <mat-error>{{ fieldError(form.controls.email, 'E-mail') }}</mat-error>
          </mat-form-field>
        </div>
        @if (form.dirty) {
          <div class="flex justify-end gap-2 pb-3">
            <button matButton type="button" (click)="reset(c)">Discard</button>
            <button matButton="filled" type="submit" [disabled]="saving()">Save changes</button>
          </div>
        }
      </form>

      <section class="qa-card overflow-hidden">
        <div class="flex items-center justify-between gap-3 px-4 py-3">
          <h3 class="m-0 text-md font-semibold">Projects</h3>
          @if (c.isActive) {
            <button matButton="outlined" type="button" (click)="addProject(c)">
              <ng-icon name="lucidePlus" size="16" class="mr-1" />New project
            </button>
          }
        </div>
        @if (projects.isLoading() && !projects.hasValue()) {
          <app-skeleton-lines [lines]="3" class="block p-4" />
        } @else if (projects.error()) {
          <app-empty-state
            kind="error"
            title="Could not load projects"
            [message]="projectsErrorMessage()"
          >
            <button matButton="outlined" type="button" (click)="projects.reload()">
              Try again
            </button>
          </app-empty-state>
        } @else if (projects.value()?.length === 0) {
          <app-empty-state
            title="No projects yet"
            message="A project gets its own key, like PGERP, which prefixes every issue number."
          >
            @if (c.isActive) {
              <button matButton="filled" type="button" (click)="addProject(c)">New project</button>
            }
          </app-empty-state>
        } @else {
          <ul class="m-0 list-none border-t border-border p-0">
            @for (project of projects.value(); track project.id) {
              <li class="border-b border-border last:border-b-0">
                <button
                  type="button"
                  class="flex min-h-14 w-full cursor-pointer items-center gap-3 border-0 bg-transparent px-4 py-2 text-left text-base text-text hover:bg-surface-muted"
                  [class.opacity-60]="!project.isActive"
                  (click)="openProject.emit(project.id)"
                >
                  <span class="w-20 shrink-0 text-sm font-bold tracking-wider text-primary">{{
                    project.key
                  }}</span>
                  <span class="min-w-0 flex-1">
                    <span class="block truncate font-medium">{{ project.name }}</span>
                    <span class="block text-xs text-muted tabular-nums"
                      >{{ project.moduleCount | plural: 'module' }},
                      {{ project.environmentCount | plural: 'environment' }},
                      {{ project.memberCount | plural: 'member'
                      }}{{ project.isActive ? '' : ' – inactive' }}</span
                    >
                  </span>
                  <ng-icon name="lucideChevronRight" size="16" class="text-muted" />
                </button>
              </li>
            }
          </ul>
        }
      </section>
    }
  `,
  host: { class: 'block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClientPanel {
  private readonly api = inject(MastersApi);
  private readonly dialogs = inject(MasterDialogs);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);

  readonly clientId = input.required<number>();
  /** Name, status or projects changed. */
  readonly changed = output<Client>();
  readonly openProject = output<number>();

  protected readonly fieldError = fieldError;
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly client = rxResource({
    params: () => this.clientId(),
    stream: ({ params }) => this.api.client(params),
  });
  protected readonly projects = rxResource({
    params: () => this.clientId(),
    stream: ({ params }) => this.api.projects(params, true),
  });
  protected readonly errorMessage = computed(() => problemMessage(this.client.error()));
  protected readonly projectsErrorMessage = computed(() => problemMessage(this.projects.error()));

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    contactPerson: ['', Validators.maxLength(100)],
    email: ['', [Validators.email, Validators.maxLength(256)]],
    phone: ['', Validators.maxLength(30)],
  });

  constructor() {
    let shownId: number | null = null;
    effect(() => {
      const client = this.client.value();
      if (client && (client.id !== shownId || !untracked(() => this.form.dirty))) {
        shownId = client.id;
        this.reset(client);
      }
    });
  }

  protected reset(client: Client): void {
    this.error.set(null);
    this.form.reset({
      name: client.name,
      contactPerson: client.contactPerson ?? '',
      email: client.email ?? '',
      phone: client.phone ?? '',
    });
  }

  protected save(client: Client): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.saving.set(true);
    this.error.set(null);
    this.api
      .updateClient(client.id, {
        name: value.name.trim(),
        contactPerson: value.contactPerson.trim() || null,
        email: value.email.trim() || null,
        phone: value.phone.trim() || null,
        rowVersion: client.rowVersion,
      })
      .subscribe({
        next: (saved) => {
          this.saving.set(false);
          this.client.set(saved);
          this.reset(saved);
          this.toast.success(`Client ${saved.name} saved.`);
          this.changed.emit(saved);
        },
        error: (error: unknown) => {
          this.saving.set(false);
          const problem = problemOf(error);
          const unmatched = applyServerErrors(this.form, problem);
          if (problem?.code === 'client_name_taken') {
            this.form.controls.name.setErrors({ server: problem.detail });
          } else if (!problem?.errors || unmatched.length) {
            this.error.set(unmatched[0] ?? problemMessage(error));
          }
        },
      });
  }

  protected async addProject(client: Client): Promise<void> {
    const result = await this.dialogs.open({
      kind: 'Project',
      parent: { id: client.id, name: client.name },
    });
    if (!result) {
      return;
    }
    if (result.isNew) {
      this.toast.success(`Project ${result.name} added.`);
    }
    this.projects.reload();
    this.client.reload();
    this.changed.emit(client);
    this.openProject.emit(result.id);
  }

  protected async setActive(client: Client, isActive: boolean): Promise<void> {
    if (!isActive) {
      const ok = await this.confirm.ask({
        title: `Deactivate ${client.name}?`,
        message:
          'The client and its projects disappear from the pickers on the Testing screen. History stays intact.',
        confirmLabel: 'Deactivate',
        danger: true,
      });
      if (!ok) {
        return;
      }
    }
    this.api.setClientActive(client.id, isActive).subscribe({
      next: (saved) => {
        this.client.set(saved);
        this.toast.success(`Client ${saved.name} ${isActive ? 'activated' : 'deactivated'}.`);
        this.changed.emit(saved);
      },
      error: (error: unknown) => this.toast.error(problemMessage(error)),
    });
  }
}
