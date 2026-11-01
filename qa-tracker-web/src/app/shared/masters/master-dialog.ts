import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleAlert, lucideTriangleAlert } from '@ng-icons/lucide';
import {
  Observable,
  catchError,
  debounceTime,
  distinctUntilChanged,
  filter,
  map,
  of,
  startWith,
  switchMap,
} from 'rxjs';
import { problemMessage, problemOf } from '../../core/api/api';
import {
  ENVIRONMENT_TYPES,
  EnvironmentType,
  MasterKind,
  MastersApi,
  Module,
  ProjectEnvironment,
  SimilarName,
} from '../data/masters.api';
import { applyServerErrors, fieldError } from '../forms/form-errors';

export interface MasterParent {
  readonly id: number;
  readonly name: string;
}

export interface MasterDialogData {
  readonly kind: MasterKind;
  /** Pre-filled name, e.g. the text typed into a picker. */
  readonly name?: string;
  /** The client of a new project; the project of a module or environment. */
  readonly parent?: MasterParent;
  /** Edit this module / environment instead of creating one. */
  readonly module?: Module;
  readonly environment?: ProjectEnvironment;
}

/** The saved item, or an existing one the user chose instead of creating a duplicate. */
export interface MasterDialogResult {
  readonly id: number;
  readonly name: string;
  readonly isNew: boolean;
}

/** Project keys: 2–8 characters, a letter first (mirrors ProjectKeys on the API). */
export const PROJECT_KEY_PATTERN = /^[A-Z][A-Z0-9]{1,7}$/;

const NOUNS: Readonly<Record<MasterKind, string>> = {
  Client: 'client',
  Project: 'project',
  Module: 'module',
  Environment: 'environment',
};

const TAKEN_CODES: Readonly<Record<string, 'name' | 'key'>> = {
  client_name_taken: 'name',
  project_name_taken: 'name',
  module_name_taken: 'name',
  environment_name_taken: 'name',
  project_key_taken: 'key',
};

/**
 * Create a client, project, module or environment with only the essential fields (plan 8.1 quick-add),
 * or edit a module / environment. Warns about similar existing names and offers to use one instead.
 */
@Component({
  selector: 'app-master-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatFormField,
    MatLabel,
    MatError,
    MatHint,
    MatInput,
    MatSelect,
    MatOption,
    MatButton,
    NgIcon,
  ],
  viewProviders: [provideIcons({ lucideCircleAlert, lucideTriangleAlert })],
  template: `
    <h2 mat-dialog-title>
      {{ editing ? 'Edit' : 'New' }} {{ noun }}
      @if (data.parent) {
        <span class="block text-sm font-normal text-muted">in {{ data.parent.name }}</span>
      }
    </h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content class="flex flex-col gap-1">
        @if (error()) {
          <div
            role="alert"
            class="mb-3 rounded-control border border-severity-critical/40 bg-severity-critical/10 px-3 py-2 text-sm text-text"
          >
            {{ error() }}
          </div>
        }

        <mat-form-field appearance="outline" class="w-full">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" autocomplete="off" cdkFocusInitial />
          <mat-error>{{ fieldError(form.controls.name, 'Name') }}</mat-error>
        </mat-form-field>

        @if (exact(); as match) {
          <div
            role="alert"
            class="mb-3 flex items-start gap-2 rounded-control border border-severity-critical/40 bg-severity-critical/10 px-3 py-2 text-sm text-text"
          >
            <ng-icon name="lucideCircleAlert" size="16" class="mt-0.5 shrink-0 text-danger" />
            <div class="min-w-0 flex-1">
              <strong class="font-semibold">{{ match.name }}</strong> already exists{{
                match.isActive ? '.' : ', but it is inactive – activate it in Masters.'
              }}
            </div>
            @if (match.isActive && !editing) {
              <button matButton type="button" class="-my-1 shrink-0" (click)="useExisting(match)">
                Use it
              </button>
            }
          </div>
        } @else if (similar().length) {
          <div
            class="mb-3 rounded-control border border-severity-medium/40 bg-severity-medium/10 px-3 py-2 text-sm text-text"
            aria-live="polite"
          >
            <div class="flex items-center gap-2 font-medium">
              <ng-icon name="lucideTriangleAlert" size="16" class="text-severity-medium" />
              Similar {{ noun }} names already exist
            </div>
            <ul class="m-0 mt-1 list-none p-0">
              @for (match of similar(); track match.id) {
                <li class="flex items-center gap-2 py-0.5 pl-6">
                  <span class="min-w-0 flex-1 truncate">{{ match.name }}</span>
                  @if (!match.isActive) {
                    <span class="text-xs text-muted">inactive</span>
                  } @else if (!editing) {
                    <button
                      matButton
                      type="button"
                      class="-my-1 shrink-0"
                      (click)="useExisting(match)"
                    >
                      Use this
                    </button>
                  }
                </li>
              }
            </ul>
          </div>
        }

        @if (data.kind === 'Project') {
          <mat-form-field appearance="outline" class="w-full">
            <mat-label>Key</mat-label>
            <input
              matInput
              formControlName="key"
              autocomplete="off"
              spellcheck="false"
              maxlength="8"
              class="font-semibold tracking-wide uppercase"
              (input)="keyTouchedByUser = true"
            />
            <mat-hint>Issues are numbered {{ form.controls.key.value || 'KEY' }}-1, -2 …</mat-hint>
            <mat-error>{{ fieldError(form.controls.key, 'Key') }}</mat-error>
          </mat-form-field>
        }

        @if (data.kind === 'Environment') {
          <mat-form-field appearance="outline" class="w-full">
            <mat-label>Type</mat-label>
            <mat-select formControlName="type">
              @for (type of environmentTypes; track type) {
                <mat-option [value]="type">{{ type }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field appearance="outline" class="w-full">
            <mat-label>URL</mat-label>
            <input
              matInput
              type="url"
              formControlName="url"
              autocomplete="off"
              placeholder="https://uat.example.com"
            />
            <mat-error>{{ fieldError(form.controls.url, 'URL') }}</mat-error>
          </mat-form-field>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>Cancel</button>
        <button matButton="filled" type="submit" [disabled]="busy() || !!exact()">
          {{ editing ? 'Save' : 'Add ' + noun }}
        </button>
      </mat-dialog-actions>
    </form>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MasterDialog {
  private readonly api = inject(MastersApi);
  private readonly dialogRef = inject<MatDialogRef<MasterDialog, MasterDialogResult>>(MatDialogRef);
  protected readonly data = inject<MasterDialogData>(MAT_DIALOG_DATA);

  private readonly existing = this.data.module ?? this.data.environment;
  protected readonly editing = this.existing !== undefined;
  protected readonly noun = NOUNS[this.data.kind];
  protected readonly environmentTypes = ENVIRONMENT_TYPES;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldError = fieldError;
  /** Stop suggesting a key once the user types their own. */
  protected keyTouchedByUser = false;

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: [
      this.existing?.name ?? this.data.name?.trim() ?? '',
      [Validators.required, Validators.maxLength(this.data.kind === 'Environment' ? 100 : 150)],
    ],
    key: [
      '',
      this.data.kind === 'Project'
        ? [Validators.required, Validators.pattern(PROJECT_KEY_PATTERN)]
        : [],
    ],
    type: [this.data.environment?.type ?? ('UAT' as EnvironmentType)],
    url: [
      this.data.environment?.url ?? '',
      this.data.kind === 'Environment'
        ? [Validators.required, Validators.maxLength(500), Validators.pattern(/^https?:\/\/\S+$/i)]
        : [],
    ],
  });

  private readonly matches = signal<SimilarName[]>([]);
  protected readonly exact = computed(() => this.matches().find((m) => m.exact) ?? null);
  protected readonly similar = computed(() => this.matches().filter((m) => !m.exact));

  constructor() {
    const destroyRef = inject(DestroyRef);
    const name$ = this.form.controls.name.valueChanges.pipe(
      startWith(this.form.controls.name.value),
      map((name) => name.trim()),
      debounceTime(250),
      distinctUntilChanged(),
      takeUntilDestroyed(destroyRef),
    );

    name$
      .pipe(switchMap((name) => this.findSimilar(name)))
      .subscribe((matches) => this.matches.set(matches.filter((m) => m.id !== this.existing?.id)));

    // The key field upper-cases as you type.
    this.form.controls.key.valueChanges.pipe(takeUntilDestroyed(destroyRef)).subscribe((key) => {
      const upper = key.toUpperCase().replace(/\s/g, '');
      if (upper !== key) {
        this.form.controls.key.setValue(upper, { emitEvent: false });
      }
    });

    const clientId = this.data.parent?.id;
    if (this.data.kind === 'Project' && clientId) {
      name$
        .pipe(
          filter((name) => name.length > 0 && !this.keyTouchedByUser),
          switchMap((name) =>
            this.api.suggestProjectKey(clientId, name).pipe(catchError(() => of(null))),
          ),
        )
        .subscribe((suggestion) => {
          if (suggestion && !this.keyTouchedByUser) {
            this.form.controls.key.setValue(suggestion.key);
          }
        });
    }
  }

  protected useExisting(match: SimilarName): void {
    this.dialogRef.close({ id: match.id, name: match.name, isNew: false });
  }

  protected save(): void {
    if (this.form.invalid || this.busy() || this.exact()) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.request().subscribe({
      next: (saved) => this.dialogRef.close({ id: saved.id, name: saved.name, isNew: true }),
      error: (error: unknown) => {
        this.busy.set(false);
        const problem = problemOf(error);
        const field = problem?.code ? TAKEN_CODES[problem.code] : undefined;
        const unmatched = applyServerErrors(this.form, problem);
        if (field) {
          this.form.controls[field].setErrors({ server: problem?.detail });
          this.form.controls[field].markAsTouched();
        } else if (!problem?.errors || unmatched.length) {
          this.error.set(unmatched[0] ?? problemMessage(error));
        }
      },
    });
  }

  private request(): Observable<{ id: number; name: string }> {
    const value = this.form.getRawValue();
    const name = value.name.trim();
    const parentId = this.data.parent?.id ?? 0;
    const environment = { name, type: value.type, url: value.url.trim() };

    switch (this.data.kind) {
      case 'Client':
        return this.api.createClient({ name });
      case 'Project':
        return this.api.createProject(parentId, {
          name,
          key: value.key,
          requireLiveVerification: false,
        });
      case 'Module':
        return this.data.module
          ? this.api.updateModule(this.data.module.id, {
              name,
              rowVersion: this.data.module.rowVersion,
            })
          : this.api.createModule(parentId, name);
      case 'Environment':
        return this.data.environment
          ? this.api.updateEnvironment(this.data.environment.id, {
              ...environment,
              rowVersion: this.data.environment.rowVersion,
            })
          : this.api.createEnvironment(parentId, environment);
    }
  }

  private findSimilar(name: string): Observable<SimilarName[]> {
    if (!name || (this.data.kind !== 'Client' && !this.data.parent)) {
      return of([]);
    }
    return this.api
      .similar(this.data.kind, name, this.data.parent?.id)
      .pipe(catchError(() => of([])));
  }
}
