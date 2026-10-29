import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { MatButtonToggle, MatButtonToggleGroup } from '@angular/material/button-toggle';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleCheck,
  lucideFileSpreadsheet,
  lucideImage,
  lucideMessageSquare,
  lucidePlus,
  lucideTriangleAlert,
  lucideUpload,
  lucideX,
} from '@ng-icons/lucide';
import { firstValueFrom } from 'rxjs';
import { problemMessage } from '../../core/api/api';
import { ToastService } from '../../core/feedback/toast.service';
import { ImportApi, ImportPreview, ImportResult, ImportRow } from '../../shared/data/import.api';
import { MastersApi } from '../../shared/data/masters.api';
import { Sprint, SprintsApi } from '../../shared/data/sprints.api';
import { MasterDialogs } from '../../shared/masters/master-dialogs.service';
import {
  IssueStatus,
  PRIORITIES,
  PRIORITY_META,
  SEVERITIES,
  SEVERITY_META,
} from '../../shared/models/issue-enums';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';
import { EntityPicker, PickerItem } from '../../shared/ui/entity-picker/entity-picker';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { StatusPill } from '../../shared/ui/status-pill/status-pill';
import { StartSprintDialog, StartSprintDialogData } from '../testing/start-sprint-dialog';
import { TestingContext } from '../testing/testing-context';
import { RowEdit, buildCorrections, initialEdit, remainingWarnings } from './import-edits';

export type ImportStep = 'file' | 'target' | 'review' | 'done';

export const IMPORT_STEPS: readonly { readonly id: ImportStep; readonly label: string }[] = [
  { id: 'file', label: 'Workbook' },
  { id: 'target', label: 'Where to' },
  { id: 'review', label: 'Review' },
  { id: 'done', label: 'Done' },
];

/** The API accepts workbooks up to 30 MB (ADR-036). */
export const MAX_WORKBOOK_BYTES = 30 * 1024 * 1024;

/** Why a chosen file cannot be imported, or null when it can. */
export function workbookProblem(file: File): string | null {
  if (!file.name.toLowerCase().endsWith('.xlsx')) {
    return 'Choose the testing workbook as an Excel .xlsx file.';
  }
  if (file.size > MAX_WORKBOOK_BYTES) {
    return 'The workbook is larger than 30 MB.';
  }
  return null;
}

/** A reviewed row as the grid shows it. */
export interface ReviewRow {
  readonly row: ImportRow;
  readonly edit: RowEdit;
  readonly warnings: readonly string[];
  readonly status: IssueStatus;
}

/** New ↔ Assigned follows the assignee chosen in the grid, as on commit. */
export function displayStatus(row: ImportRow, edit: RowEdit): IssueStatus {
  if (row.status === 'New' && edit.assigneeId !== null) {
    return 'Assigned';
  }
  if (row.status === 'Assigned' && edit.assigneeId === null) {
    return 'New';
  }
  return row.status;
}

/**
 * Import from Excel (plan 8.2, S27): upload the legacy testing workbook → choose (or quick-add) client, project and a
 * sprint in Testing → review the parsed rows with their warnings and correct them → commit → summary with links.
 */
@Component({
  selector: 'app-import-page',
  imports: [
    MatButton,
    MatButtonToggle,
    MatButtonToggleGroup,
    MatCheckbox,
    MatFormField,
    MatInput,
    MatLabel,
    MatOption,
    MatSelect,
    RouterLink,
    NgIcon,
    EntityPicker,
    FileSizePipe,
    PageHeader,
    StatusPill,
  ],
  viewProviders: [
    provideIcons({
      lucideCircleCheck,
      lucideFileSpreadsheet,
      lucideImage,
      lucideMessageSquare,
      lucidePlus,
      lucideTriangleAlert,
      lucideUpload,
      lucideX,
    }),
  ],
  templateUrl: './import-page.html',
  styleUrl: './import-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ImportPage {
  private readonly api = inject(ImportApi);
  private readonly masters = inject(MastersApi);
  private readonly sprintsApi = inject(SprintsApi);
  private readonly masterDialogs = inject(MasterDialogs);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  protected readonly steps = IMPORT_STEPS;
  protected readonly severities = SEVERITIES;
  protected readonly severityMeta = SEVERITY_META;
  protected readonly priorities = PRIORITIES;
  protected readonly priorityMeta = PRIORITY_META;

  protected readonly step = signal<ImportStep>('file');
  protected readonly stepIndex = computed(() =>
    IMPORT_STEPS.findIndex((s) => s.id === this.step()),
  );

  // Step 1 – workbook
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal<string | null>(null);
  protected readonly dragging = signal(false);

  // Step 2 – where to (starts from the Testing screen's remembered client and project)
  private readonly remembered = inject(TestingContext).load();
  protected readonly clientId = signal<number | null>(this.remembered.clientId);
  protected readonly projectId = signal<number | null>(this.remembered.projectId);

  protected readonly cascade = rxResource({
    params: () => ({ clientId: this.clientId(), projectId: this.projectId() }),
    stream: ({ params }) => this.masters.cascade(params.clientId, params.projectId),
  });
  protected readonly sprints = rxResource({
    params: () => this.projectId() ?? undefined,
    stream: ({ params }) => this.sprintsApi.list(params),
  });
  /** Issues are only added while a sprint is in Testing – there is at most one per project. */
  protected readonly sprint = computed<Sprint | null>(() =>
    this.sprints.hasValue()
      ? (this.sprints.value().find((s) => s.status === 'Testing') ?? null)
      : null,
  );
  protected readonly clientItems = computed<PickerItem[]>(() =>
    (this.cascade.value()?.clients ?? []).map((c) => ({ id: c.id, label: c.name })),
  );
  protected readonly projectItems = computed<PickerItem[]>(() =>
    (this.cascade.value()?.projects ?? []).map((p) => ({ id: p.id, label: p.name, hint: p.key })),
  );
  protected readonly addClient = this.masterDialogs.quickAdd('Client');
  protected readonly addProject = this.masterDialogs.quickAdd('Project', () => {
    const id = this.clientId();
    const client = this.cascade.value()?.clients.find((c) => c.id === id);
    return client ? { id: client.id, name: client.name } : null;
  });

  // Step 3 – review
  protected readonly reading = signal(false);
  protected readonly readError = signal<string | null>(null);
  protected readonly preview = signal<ImportPreview | null>(null);
  protected readonly edits = signal<ReadonlyMap<string, RowEdit>>(new Map());
  protected readonly moduleNames = signal<ReadonlyMap<number, string>>(new Map());
  protected readonly filter = signal<'all' | 'warnings'>('all');

  /** Developers of the project (and whoever "Resolve By" matched) can be chosen as assignee. */
  protected readonly assignees = computed(() => {
    const members = (this.cascade.value()?.members ?? [])
      .filter((m) => m.role !== 'Tester')
      .map((m) => ({ id: m.userId, name: m.fullName }));
    for (const row of this.preview()?.rows ?? []) {
      if (row.assigneeId !== null && !members.some((m) => m.id === row.assigneeId)) {
        members.push({ id: row.assigneeId, name: row.assigneeName ?? `User ${row.assigneeId}` });
      }
    }
    return members.sort((a, b) => a.name.localeCompare(b.name));
  });

  protected readonly reviewRows = computed<ReviewRow[]>(() => {
    const edits = this.edits();
    return (this.preview()?.rows ?? []).map((row) => {
      const edit = edits.get(row.id) ?? initialEdit(row);
      return {
        row,
        edit,
        warnings: remainingWarnings(row, edit),
        status: displayStatus(row, edit),
      };
    });
  });
  protected readonly shownRows = computed(() =>
    this.filter() === 'warnings'
      ? this.reviewRows().filter((r) => r.edit.include && r.warnings.length > 0)
      : this.reviewRows(),
  );
  protected readonly counts = computed(() => {
    const rows = this.reviewRows();
    const included = rows.filter((r) => r.edit.include);
    const ready = included.filter((r) => r.warnings.length === 0).length;
    return {
      total: rows.length,
      included: included.length,
      skipped: rows.length - included.length,
      ready,
      needsReview: included.length - ready,
      readyPercent: included.length ? Math.round((100 * ready) / included.length) : 0,
      images: included.reduce((sum, r) => sum + r.row.images, 0),
    };
  });

  // Step 4 – done
  protected readonly committing = signal(false);
  protected readonly commitError = signal<string | null>(null);
  protected readonly result = signal<ImportResult | null>(null);
  protected readonly committedSprint = signal<Sprint | null>(null);

  // ---- step 1 ----

  protected chooseFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.setFile(input.files?.[0] ?? null);
    input.value = '';
  }

  protected drop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.setFile(event.dataTransfer?.files?.[0] ?? null);
  }

  protected dragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected setFile(file: File | null): void {
    if (!file) {
      return;
    }
    const problem = workbookProblem(file);
    this.fileError.set(problem);
    this.file.set(problem ? null : file);
    this.preview.set(null);
  }

  protected clearFile(): void {
    this.file.set(null);
    this.fileError.set(null);
    this.preview.set(null);
  }

  // ---- step 2 ----

  protected chooseClient(id: number | null): void {
    if (id !== this.clientId()) {
      this.clientId.set(id);
      this.projectId.set(null);
    }
  }

  protected chooseProject(id: number | null): void {
    this.projectId.set(id);
  }

  protected async startSprint(): Promise<void> {
    const projectId = this.projectId();
    const cascade = this.cascade.value();
    const project = cascade?.projects.find((p) => p.id === projectId);
    if (!project || !cascade) {
      return;
    }
    const previous = this.sprints.value()?.[0];
    const ref = this.dialog.open<StartSprintDialog, StartSprintDialogData, Sprint>(
      StartSprintDialog,
      {
        data: {
          projectId: project.id,
          projectName: project.name,
          environments: cascade.environments,
          phaseId: previous?.phaseId,
          defaultEnvironmentId: previous?.defaultEnvironmentId,
        },
        width: '480px',
        maxWidth: 'calc(100vw - 32px)',
        restoreFocus: true,
      },
    );
    const sprint = await firstValueFrom(ref.afterClosed());
    if (sprint) {
      this.toast.success(`${sprint.name} started.`);
      this.sprints.reload();
    }
  }

  /** Reads the workbook into the preview grid (nothing is saved yet). */
  protected readWorkbook(): void {
    const file = this.file();
    const sprint = this.sprint();
    if (!file || !sprint) {
      return;
    }
    this.reading.set(true);
    this.readError.set(null);
    this.api.preview(sprint.id, file).subscribe({
      next: (preview) => {
        this.preview.set(preview);
        this.edits.set(new Map());
        this.moduleNames.set(new Map(preview.modules.map((m) => [m.sheetIndex, m.name])));
        this.filter.set(preview.readyRows < preview.rows.length ? 'warnings' : 'all');
        this.reading.set(false);
        this.step.set('review');
      },
      error: (error: unknown) => {
        this.reading.set(false);
        this.readError.set(problemMessage(error, 'The workbook could not be read.'));
      },
    });
  }

  // ---- step 3 ----

  protected edit(row: ImportRow, change: Partial<RowEdit>): void {
    this.edits.update((edits) => {
      const next = new Map(edits);
      next.set(row.id, { ...(edits.get(row.id) ?? initialEdit(row)), ...change });
      return next;
    });
  }

  protected renameModule(sheetIndex: number, name: string): void {
    this.moduleNames.update((names) => new Map(names).set(sheetIndex, name));
  }

  protected moduleName(sheetIndex: number): string {
    return this.moduleNames().get(sheetIndex) ?? '';
  }

  protected setAll(include: boolean): void {
    this.edits.update((edits) => {
      const next = new Map(edits);
      for (const row of this.preview()?.rows ?? []) {
        next.set(row.id, { ...(edits.get(row.id) ?? initialEdit(row)), include });
      }
      return next;
    });
  }

  protected commit(): void {
    const preview = this.preview();
    const file = this.file();
    const sprint = this.sprint();
    if (!preview || !file || !sprint || this.counts().included === 0) {
      return;
    }
    this.committing.set(true);
    this.commitError.set(null);
    this.api
      .commit(sprint.id, file, buildCorrections(preview, this.edits(), this.moduleNames()))
      .subscribe({
        next: (result) => {
          this.committing.set(false);
          this.result.set(result);
          this.committedSprint.set(sprint);
          this.step.set('done');
        },
        error: (error: unknown) => {
          this.committing.set(false);
          this.commitError.set(problemMessage(error, 'The issues could not be imported.'));
        },
      });
  }

  // ---- navigation ----

  protected goTo(step: ImportStep): void {
    if (this.step() === 'done') {
      return;
    }
    if (step === 'target' && !this.file()) {
      return;
    }
    if (step === 'review' && !this.preview()) {
      return;
    }
    this.step.set(step);
  }

  protected startOver(): void {
    this.file.set(null);
    this.preview.set(null);
    this.result.set(null);
    this.edits.set(new Map());
    this.commitError.set(null);
    this.readError.set(null);
    this.sprints.reload();
    this.step.set('file');
  }
}
