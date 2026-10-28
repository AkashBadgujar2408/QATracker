import { IssueTimeline } from '../../shared/timeline/issue-timeline';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RichTextEditor } from '../../shared/ui/rich-text-editor/rich-text-editor';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  linkedSignal,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { ConfirmService } from '../../core/feedback/confirm.service';
import { ToastService } from '../../core/feedback/toast.service';
import { ThemeService } from '../../core/theme/theme.service';
import {
  ISSUE_STATUSES,
  ISSUE_STATUS_META,
  PRIORITIES,
  SEVERITIES,
  SEVERITY_META,
} from '../../shared/models/issue-enums';
import { Cascade, MastersApi } from '../../shared/data/masters.api';
import { MasterDialogs } from '../../shared/masters/master-dialogs.service';
import { EntityPicker, PickerItem } from '../../shared/ui/entity-picker/entity-picker';
import { IssueAttachments } from '../../shared/attachments/issue-attachments';
import { PasteDropzone } from '../../shared/ui/paste-dropzone/paste-dropzone';
import { Breadcrumb } from '../../shared/ui/breadcrumb/breadcrumb';
import { Logo } from '../../shared/ui/logo/logo';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { PriorityIcon } from '../../shared/ui/priority-icon/priority-icon';
import { SeverityBadge } from '../../shared/ui/severity-badge/severity-badge';
import { SkeletonLines, SkeletonTable } from '../../shared/ui/skeleton/skeleton';
import { StatusPill } from '../../shared/ui/status-pill/status-pill';
import { UserAvatar } from '../../shared/ui/user-avatar/user-avatar';

interface Swatch {
  readonly label: string;
  readonly token: string;
}

/** Hidden living style guide (/dev/ui): every shared component in the current theme. */
@Component({
  selector: 'app-ui-showcase',
  imports: [
    MatButton,
    PageHeader,
    Breadcrumb,
    Logo,
    EmptyState,
    StatusPill,
    SeverityBadge,
    PriorityIcon,
    UserAvatar,
    SkeletonLines,
    SkeletonTable,
    EntityPicker,
    IssueAttachments,
    PasteDropzone,
    RichTextEditor,
    IssueTimeline,
    ReactiveFormsModule,
  ],
  templateUrl: './ui-showcase.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UiShowcase {
  protected readonly theme = inject(ThemeService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  /** app-rich-text-editor demo: the value is sanitised HTML (null when empty). */
  /** app-issue-timeline demo: live data of an existing issue (pick one with comments). */
  protected readonly timelineKey = signal('PGERP-1');

  protected readonly richText = new FormControl<string | null>(
    '<p>Open <b>Sales Return Entry</b> and:</p><ol><li>Pick tomorrow as the return date</li><li>Press <code>Save</code></li></ol>',
  );

  protected readonly statuses = ISSUE_STATUSES;
  protected readonly severities = SEVERITIES;
  protected readonly priorities = PRIORITIES;
  protected readonly people = [
    'John Carter',
    'Emily Clark',
    'Michael Brown',
    'Sarah Miller',
    'Olivia',
  ];
  protected readonly lastConfirm = signal<string>('–');

  // Attachments: live against an existing issue (upload needs its sprint in Testing).
  protected readonly attachmentIssueKey = signal('PGERP-1');
  protected readonly pendingFiles = signal<readonly File[]>([]);

  // entity-picker: live Client → Project → Module → Environment cascade (real API, quick-add creates real masters).
  private readonly masters = inject(MastersApi);
  private readonly masterDialogs = inject(MasterDialogs);
  protected readonly pickedClient = signal<number | null>(null);
  protected readonly pickedProject = signal<number | null>(null);
  protected readonly pickedModule = signal<number | null>(null);
  protected readonly pickedEnvironment = signal<number | null>(null);
  protected readonly cascade = rxResource({
    params: () => ({ clientId: this.pickedClient(), projectId: this.pickedProject() }),
    stream: ({ params }) => this.masters.cascade(params.clientId, params.projectId),
  });
  /** Keeps the previous lists while the next cascade loads (no label flicker). */
  private readonly lists = linkedSignal<Cascade | undefined, Cascade | undefined>({
    source: () => (this.cascade.hasValue() ? this.cascade.value() : undefined),
    computation: (value, previous) => value ?? previous?.value,
  });
  protected readonly clientItems = computed<PickerItem[]>(() =>
    (this.lists()?.clients ?? []).map((c) => ({ id: c.id, label: c.name })),
  );
  protected readonly projectItems = computed<PickerItem[]>(() =>
    this.pickedClient()
      ? (this.lists()?.projects ?? []).map((p) => ({ id: p.id, label: p.name, hint: p.key }))
      : [],
  );
  protected readonly moduleItems = computed<PickerItem[]>(() =>
    this.pickedProject()
      ? (this.lists()?.modules ?? []).map((m) => ({ id: m.id, label: m.name }))
      : [],
  );
  protected readonly environmentItems = computed<PickerItem[]>(() =>
    this.pickedProject()
      ? (this.lists()?.environments ?? []).map((e) => ({ id: e.id, label: e.name, hint: e.url }))
      : [],
  );
  private readonly parentClient = () =>
    this.clientItems().find((c) => c.id === this.pickedClient()) ?? null;
  private readonly parentProject = () =>
    this.projectItems().find((p) => p.id === this.pickedProject()) ?? null;
  protected readonly addClient = this.masterDialogs.quickAdd('Client');
  protected readonly addProject = this.masterDialogs.quickAdd('Project', () => {
    const client = this.parentClient();
    return client && { id: client.id, name: client.label };
  });
  protected readonly addModule = this.masterDialogs.quickAdd('Module', () => {
    const project = this.parentProject();
    return project && { id: project.id, name: project.label };
  });
  protected readonly addEnvironment = this.masterDialogs.quickAdd('Environment', () => {
    const project = this.parentProject();
    return project && { id: project.id, name: project.label };
  });

  protected pickClient(id: number | null): void {
    this.pickedClient.set(id);
    this.pickProject(null);
  }

  protected pickProject(id: number | null): void {
    this.pickedProject.set(id);
    this.pickedModule.set(null);
    this.pickedEnvironment.set(null);
  }

  protected readonly surfaceSwatches: readonly Swatch[] = [
    { label: 'Primary', token: '--qa-primary' },
    { label: 'Background', token: '--qa-bg' },
    { label: 'Surface', token: '--qa-surface' },
    { label: 'Surface muted', token: '--qa-surface-muted' },
    { label: 'Border', token: '--qa-border' },
    { label: 'Text', token: '--qa-text' },
    { label: 'Text muted', token: '--qa-text-muted' },
  ];
  protected readonly severitySwatches: readonly Swatch[] = SEVERITIES.map((s) => ({
    label: SEVERITY_META[s].label,
    token: SEVERITY_META[s].colorToken,
  }));
  protected readonly statusSwatches: readonly Swatch[] = ISSUE_STATUSES.map((s) => ({
    label: ISSUE_STATUS_META[s].label,
    token: ISSUE_STATUS_META[s].colorToken,
  }));
  protected readonly sprintSwatches: readonly Swatch[] = [
    { label: 'Testing', token: '--qa-sprint-testing' },
    { label: 'Testing Completed', token: '--qa-sprint-testing-completed' },
    { label: 'Closed', token: '--qa-sprint-closed' },
  ];
  protected readonly typeScale = [
    { label: 'Caption 12', cls: 'text-xs' },
    { label: 'Small 13', cls: 'text-sm' },
    { label: 'Body 14', cls: 'text-base' },
    { label: 'Medium 16', cls: 'text-md' },
    { label: 'Section 20', cls: 'text-lg' },
    { label: 'Title 24', cls: 'text-xl' },
    { label: 'Page 30', cls: 'text-2xl' },
  ];
  protected readonly sampleTrail = [
    { label: 'Pardi Gears', link: '/dev/ui' },
    { label: 'PGERP', link: '/dev/ui' },
    { label: 'Sprint 1 – UAT' },
  ];

  protected showToast(kind: 'success' | 'info' | 'warn' | 'error'): void {
    const messages = {
      success: 'PGERP-142 assigned to Emily.',
      info: 'Sprint 1 – UAT is still in testing.',
      warn: '3 issues have severity "Not assessed".',
      error: 'PGERP-142 was changed by Michael. Reload to see the latest version.',
    };
    this.toast[kind](messages[kind]);
  }

  protected async askConfirm(danger: boolean): Promise<void> {
    const confirmed = await this.confirm.ask(
      danger
        ? {
            title: 'Delete PGERP-142?',
            message: 'The issue and its attachments are removed permanently.',
            confirmLabel: 'Delete',
            danger: true,
          }
        : {
            title: 'Mark testing completed?',
            message: 'Developers will be notified and can start picking issues.',
            confirmLabel: 'Mark completed',
          },
    );
    this.lastConfirm.set(confirmed ? 'Confirmed' : 'Cancelled');
  }
}
