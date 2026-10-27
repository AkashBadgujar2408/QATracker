import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
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
  ],
  templateUrl: './ui-showcase.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UiShowcase {
  protected readonly theme = inject(ThemeService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

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
