import { HttpEventType } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  MatAutocomplete,
  MatAutocompleteTrigger,
  MatOption as MatAutocompleteOption,
} from '@angular/material/autocomplete';
import { MatButton } from '@angular/material/button';
import { MatButtonToggle, MatButtonToggleGroup } from '@angular/material/button-toggle';
import { MatError, MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCopy, lucidePencil, lucideX } from '@ng-icons/lucide';
import { EMPTY, catchError, debounceTime, filter, last, of } from 'rxjs';
import { problemMessage, problemOf } from '../../core/api/api';
import { ToastService } from '../../core/feedback/toast.service';
import { IssueAttachments } from '../../shared/attachments/issue-attachments';
import { AttachmentsApi } from '../../shared/data/attachments.api';
import { Issue, IssueFields, IssuesApi } from '../../shared/data/issues.api';
import { Cascade } from '../../shared/data/masters.api';
import { Sprint } from '../../shared/data/sprints.api';
import { applyServerErrors, fieldError } from '../../shared/forms/form-errors';
import { MasterDialogs } from '../../shared/masters/master-dialogs.service';
import {
  ISSUE_TYPES,
  IssueType,
  PRIORITIES,
  PRIORITY_META,
  Priority,
  SEVERITIES,
  SEVERITY_META,
  Severity,
} from '../../shared/models/issue-enums';
import { EntityPicker, PickerItem } from '../../shared/ui/entity-picker/entity-picker';
import { PasteDropzone } from '../../shared/ui/paste-dropzone/paste-dropzone';
import { RichTextEditor } from '../../shared/ui/rich-text-editor/rich-text-editor';

/** What the form is doing: a new issue (optionally cloned from one), or editing an existing one. */
export type IssueFormMode =
  | { readonly kind: 'create'; readonly cloneOf?: Issue }
  | { readonly kind: 'edit'; readonly issue: Issue };

export interface IssueSaved {
  readonly issue: Issue;
  readonly created: boolean;
}

const TYPE_LABELS: Readonly<Record<IssueType, string>> = {
  Bug: 'Bug',
  Validation: 'Validation',
  UI: 'UI',
  Security: 'Security',
  Performance: 'Performance',
  Enhancement: 'Enhancement',
  Clarification: 'Clarification',
};

/**
 * Quick issue form of the Testing workspace (plan 8.1). Ctrl+Enter = Save & add another (keeps module, screen, type
 * and environment); Ctrl+V anywhere on the page adds a screenshot. Pending files upload right after the issue is
 * created; in edit mode attachments upload at once.
 */
@Component({
  selector: 'app-issue-form',
  imports: [
    ReactiveFormsModule,
    MatAutocomplete,
    MatAutocompleteTrigger,
    MatAutocompleteOption,
    MatButton,
    MatButtonToggle,
    MatButtonToggleGroup,
    MatError,
    MatFormField,
    MatLabel,
    MatInput,
    MatSelect,
    MatOption,
    NgIcon,
    EntityPicker,
    PasteDropzone,
    RichTextEditor,
    IssueAttachments,
  ],
  viewProviders: [provideIcons({ lucideCopy, lucidePencil, lucideX })],
  templateUrl: './issue-form.html',
  styles: `
    /* Pinned to the very bottom of the screen: the page (main) has 2rem bottom padding that sticky would keep free. */
    .qa-form-footer {
      bottom: -2rem;
      box-shadow: 0 -4px 12px -6px color-mix(in srgb, var(--qa-slate-900) 14%, transparent);
    }
    .qa-kbd {
      display: inline-block;
      min-width: 1.25rem;
      padding: 0 0.3rem;
      border: 1px solid var(--qa-border-strong);
      border-bottom-width: 2px;
      border-radius: 4px;
      font: inherit;
      font-size: 0.8em;
      text-align: center;
      color: var(--qa-text);
      background: var(--qa-surface-muted);
    }
    .qa-toggles {
      --mat-button-toggle-height: 34px;
      --mat-button-toggle-label-text-size: var(--qa-text-sm);
      --mat-button-toggle-selected-state-background-color: color-mix(
        in srgb,
        var(--qa-primary) 14%,
        transparent
      );
      --mat-button-toggle-selected-state-text-color: var(--qa-text);
      --mat-button-toggle-shape: var(--qa-radius-control);
    }
    .qa-toggles .mat-button-toggle-checked {
      font-weight: 600;
    }
    .ng-invalid.ng-touched.qa-toggles {
      border-color: var(--qa-severity-critical);
    }
  `,
  host: {
    class: 'block',
    '(keydown.control.enter)': 'onCtrlEnter($event)',
    '(keydown.meta.enter)': 'onCtrlEnter($event)',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IssueForm {
  readonly sprint = input.required<Sprint>();
  readonly cascade = input.required<Cascade>();
  readonly mode = input<IssueFormMode>({ kind: 'create' });
  /** Disables everything (sprint not in Testing). */
  readonly readOnly = input(false);

  readonly saved = output<IssueSaved>();
  /** Edit / clone finished or cancelled – back to a fresh form. */
  readonly closed = output<void>();
  /** A module or environment was quick-added – reload the cascade. */
  readonly mastersChanged = output<void>();

  private readonly fb = inject(NonNullableFormBuilder);
  private readonly issuesApi = inject(IssuesApi);
  private readonly attachmentsApi = inject(AttachmentsApi);
  private readonly toast = inject(ToastService);
  protected readonly masterDialogs = inject(MasterDialogs);

  protected readonly fieldError = fieldError;
  protected readonly types = ISSUE_TYPES;
  protected readonly typeLabels = TYPE_LABELS;
  protected readonly severities = SEVERITIES;
  protected readonly severityMeta = SEVERITY_META;
  protected readonly priorities = PRIORITIES;
  protected readonly priorityMeta = PRIORITY_META;

  protected readonly form = this.fb.group({
    moduleId: this.fb.control<number | null>(null, Validators.required),
    screenName: this.fb.control('', Validators.maxLength(150)),
    foundOnEnvironmentId: this.fb.control<number | null>(null),
    pageUrl: this.fb.control('', [
      Validators.maxLength(1000),
      Validators.pattern(/^https?:\/\/\S+$/i),
    ]),
    type: this.fb.control<IssueType | null>('Bug', Validators.required),
    title: this.fb.control('', [Validators.required, Validators.maxLength(200)]),
    steps: this.fb.control<string | null>(null),
    expectedResult: this.fb.control<string | null>(null),
    actualResult: this.fb.control<string | null>(null, Validators.required),
    impact: this.fb.control<string | null>(null),
    severity: this.fb.control<Severity | null>(null, Validators.required),
    priority: this.fb.control<Priority | null>(null, Validators.required),
    assigneeId: this.fb.control<number | null>(null),
  });

  protected readonly pendingFiles = signal<readonly File[]>([]);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  /** 409: someone else changed the issue. */
  protected readonly conflict = signal(false);

  protected readonly editing = computed(() => {
    const mode = this.mode();
    return mode.kind === 'edit' ? mode.issue : null;
  });
  protected readonly cloneOf = computed(() => {
    const mode = this.mode();
    return mode.kind === 'create' ? (mode.cloneOf ?? null) : null;
  });

  protected readonly moduleItems = computed<PickerItem[]>(() =>
    this.cascade().modules.map((m) => ({ id: m.id, label: m.name })),
  );
  protected readonly environmentItems = computed<PickerItem[]>(() =>
    this.cascade().environments.map((e) => ({ id: e.id, label: e.name, hint: e.type })),
  );
  /** Issues go to developers (or admins), never to testers. */
  protected readonly assignees = computed(() =>
    this.cascade().members.filter((m) => m.role !== 'Tester'),
  );

  /** Reloading the sprint (new counts) must not reset the form – only another sprint does. */
  private readonly sprintId = computed(() => this.sprint().id);

  protected readonly projectParent = computed(() => {
    const sprint = this.sprint();
    return { id: sprint.projectId, name: sprint.projectName };
  });
  protected readonly addModule = this.masterDialogs.quickAdd('Module', () => this.projectParent());
  protected readonly addEnvironment = this.masterDialogs.quickAdd('Environment', () =>
    this.projectParent(),
  );

  private readonly moduleId = toSignal(this.form.controls.moduleId.valueChanges, {
    initialValue: null,
  });
  private readonly screenText = toSignal(
    this.form.controls.screenName.valueChanges.pipe(debounceTime(150)),
    { initialValue: '' },
  );
  private readonly screenList = rxResource({
    params: () => ({ projectId: this.sprint().projectId, moduleId: this.moduleId() }),
    stream: ({ params }) =>
      this.issuesApi.screens(params.projectId, params.moduleId).pipe(catchError(() => of([]))),
  });
  /** Screen suggestions (plan 7.1): the module's screens first, filtered by what was typed. */
  protected readonly screenSuggestions = computed(() => {
    const text = this.screenText().trim().toLowerCase();
    const screens = this.screenList.value() ?? [];
    return (text ? screens.filter((s) => s.toLowerCase().includes(text)) : screens)
      .filter((s) => s.toLowerCase() !== text)
      .slice(0, 8);
  });

  private readonly titleInput = viewChild.required<ElementRef<HTMLInputElement>>('titleInput');
  private readonly modulePicker = viewChild.required(EntityPicker);

  constructor() {
    // Load the issue (edit / clone) or start fresh when the mode or sprint changes.
    effect(() => {
      const mode = this.mode();
      this.sprintId();
      untracked(() => this.load(mode, this.sprint()));
    });

    effect(() => {
      const readOnly = this.readOnly();
      untracked(() => (readOnly ? this.form.disable() : this.form.enable()));
    });
  }

  /** Focus the first empty field – the "N" shortcut. */
  focusFirst(): void {
    if (this.form.controls.moduleId.value === null) {
      this.modulePicker().focus();
    } else {
      this.titleInput().nativeElement.focus();
    }
  }

  protected onCtrlEnter(event: Event): void {
    event.preventDefault();
    this.save(this.editing() === null);
  }

  protected chooseScreen(name: string): void {
    this.form.controls.screenName.setValue(name);
  }

  protected cancel(): void {
    this.closed.emit();
  }

  /** Saves; for a new issue `again` keeps module, screen, type and environment for the next one. */
  protected save(again: boolean): void {
    this.form.markAllAsTouched();
    if (this.saving() || this.readOnly()) {
      return;
    }
    if (this.form.invalid) {
      this.error.set('Fill in the highlighted fields.');
      this.focusFirstInvalid();
      return;
    }
    const fields = this.fields();
    const editing = this.editing();
    this.saving.set(true);
    this.error.set(null);
    this.conflict.set(false);

    const request = editing
      ? this.issuesApi.update(editing.key, editing.rowVersion, fields)
      : this.issuesApi.create(this.sprint().id, fields);

    request.subscribe({
      next: (issue) => {
        this.saving.set(false);
        if (editing) {
          this.toast.success(`${issue.key} saved.`);
          this.saved.emit({ issue, created: false });
          return;
        }
        this.uploadPending(issue);
        this.toast.success(`${issue.key} added.`);
        this.saved.emit({ issue, created: true });
        if (this.cloneOf()) {
          this.closed.emit();
        }
        this.clear(again);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        const problem = problemOf(error);
        if (problem?.status === 409) {
          this.conflict.set(true);
          this.error.set(problemMessage(error));
          return;
        }
        const unmatched = applyServerErrors(this.form, problem);
        this.error.set(
          unmatched.length
            ? unmatched.join(' ')
            : problem?.errors
              ? 'Fix the highlighted fields.'
              : problemMessage(error),
        );
      },
    });
  }

  /** 409: load the latest version of the issue being edited (discards the user's changes). */
  protected reloadLatest(): void {
    const editing = this.editing();
    if (!editing) {
      return;
    }
    this.issuesApi.get(editing.key).subscribe({
      next: (issue) => {
        this.conflict.set(false);
        this.error.set(null);
        this.load({ kind: 'edit', issue }, this.sprint());
      },
      error: (error: unknown) => this.error.set(problemMessage(error)),
    });
  }

  private uploadPending(issue: Issue): void {
    const files = this.pendingFiles();
    if (!files.length) {
      return;
    }
    this.attachmentsApi
      .upload(issue.key, files)
      .pipe(
        filter((event) => event.type === HttpEventType.Response),
        last(),
        catchError((error: unknown) => {
          this.toast.error(
            `${issue.key} was saved, but its attachments were not: ${problemMessage(error)}`,
          );
          return EMPTY;
        }),
      )
      .subscribe();
  }

  private fields(): IssueFields {
    const v = this.form.getRawValue();
    const text = (value: string) => value.trim() || null;
    return {
      moduleId: v.moduleId!,
      screenName: text(v.screenName),
      foundOnEnvironmentId: v.foundOnEnvironmentId,
      pageUrl: text(v.pageUrl),
      type: v.type!,
      title: v.title.trim(),
      steps: v.steps,
      expectedResult: v.expectedResult,
      actualResult: v.actualResult ?? '',
      impact: v.impact,
      severity: v.severity!,
      priority: v.priority!,
      assigneeId: v.assigneeId,
    };
  }

  private load(mode: IssueFormMode, sprint: Sprint): void {
    this.error.set(null);
    this.conflict.set(false);
    this.pendingFiles.set([]);
    const source = mode.kind === 'edit' ? mode.issue : mode.cloneOf;
    if (!source) {
      this.form.reset({ foundOnEnvironmentId: sprint.defaultEnvironmentId, type: 'Bug' });
      return;
    }
    this.form.reset({
      moduleId: source.moduleId,
      screenName: source.screenName ?? '',
      foundOnEnvironmentId:
        mode.kind === 'edit' ? source.foundOnEnvironmentId : sprint.defaultEnvironmentId,
      pageUrl: source.pageUrl ?? '',
      type: source.type,
      title: source.title,
      steps: source.steps,
      expectedResult: source.expectedResult,
      actualResult: source.actualResult,
      impact: source.impact,
      severity: source.severity,
      priority: source.priority,
      assigneeId: source.assigneeId,
    });
    queueMicrotask(() => this.titleInput().nativeElement.focus());
  }

  private clear(keepContext: boolean): void {
    const v = this.form.getRawValue();
    this.pendingFiles.set([]);
    this.form.reset(
      keepContext
        ? {
            moduleId: v.moduleId,
            screenName: v.screenName,
            type: v.type,
            foundOnEnvironmentId: v.foundOnEnvironmentId,
          }
        : { foundOnEnvironmentId: this.sprint().defaultEnvironmentId, type: 'Bug' },
    );
    queueMicrotask(() => this.focusFirst());
  }

  private focusFirstInvalid(): void {
    queueMicrotask(() => {
      const host = this.titleInput().nativeElement.closest('form');
      const target = host?.querySelector<HTMLElement>(
        '.mat-form-field-invalid input, .mat-form-field-invalid mat-select, [aria-invalid="true"], .ng-invalid.mat-button-toggle-group button',
      );
      target?.focus();
    });
  }
}
