import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideExternalLink, lucideLink, lucideX } from '@ng-icons/lucide';
import { firstValueFrom, map } from 'rxjs';
import { problemMessage } from '../../core/api/api';
import { ToastService } from '../../core/feedback/toast.service';
import { reloadOnIssueChanges } from '../../core/realtime/realtime.service';
import { Issue, IssuesApi } from '../../shared/data/issues.api';
import {
  RetestGroup,
  RetestItem,
  WaitBucket,
  WorkApi,
  waitBucket,
  waitLabel,
  waitingDays,
} from '../../shared/data/work.api';
import { IstDatePipe } from '../../shared/pipes/ist-date.pipe';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { SeverityBadge } from '../../shared/ui/severity-badge/severity-badge';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';
import { ActionDialog, ActionDialogData } from '../issues/action-dialog';

/** Text colour per ageing bucket – the longer a fix waits, the louder it gets. */
export const WAIT_CLASS: Readonly<Record<WaitBucket, string>> = {
  fresh: 'text-muted',
  days: 'text-text',
  aging: 'text-status-on-hold font-medium',
  stale: 'text-severity-critical font-semibold',
};

/**
 * Retest queue (plan 7.4, 8.2): pending deployments grouped by environment URL – open the environment once, then
 * Pass / Fail each fix. Fail asks what still fails (+ screenshot) and reopens the issue.
 */
@Component({
  selector: 'app-retest-queue-page',
  imports: [
    MatButton,
    MatSlideToggle,
    RouterLink,
    NgIcon,
    IstDatePipe,
    EmptyState,
    PageHeader,
    SeverityBadge,
    SkeletonLines,
  ],
  viewProviders: [provideIcons({ lucideCheck, lucideExternalLink, lucideLink, lucideX })],
  templateUrl: './retest-queue-page.html',
  // Status hues are too light for text on their own – mixed with the text colour for contrast.
  styles: `
    .qa-pass {
      --mat-button-outlined-label-text-color: color-mix(
        in oklab,
        var(--qa-status-verified) 65%,
        var(--qa-text)
      );
      --mat-button-outlined-outline-color: color-mix(
        in oklab,
        var(--qa-status-verified) 55%,
        transparent
      );
    }
    .qa-fail {
      --mat-button-outlined-label-text-color: color-mix(
        in oklab,
        var(--qa-status-reopened) 75%,
        var(--qa-text)
      );
      --mat-button-outlined-outline-color: color-mix(
        in oklab,
        var(--qa-status-reopened) 50%,
        transparent
      );
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RetestQueuePage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly workApi = inject(WorkApi);
  private readonly issues = inject(IssuesApi);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  protected readonly mine = toSignal(
    this.route.queryParamMap.pipe(map((p) => p.get('mine') === '1')),
    { initialValue: this.route.snapshot.queryParamMap.get('mine') === '1' },
  );
  protected readonly queue = rxResource({
    params: () => this.mine(),
    stream: ({ params }) => this.workApi.retestQueue(params),
  });

  constructor() {
    reloadOnIssueChanges(() => this.queue.reload());
  }
  protected readonly groups = computed(() =>
    this.queue.hasValue() ? this.queue.value().groups : [],
  );
  protected readonly count = computed(() => (this.queue.hasValue() ? this.queue.value().count : 0));
  /** The deployment being retested (buttons disabled meanwhile). */
  protected readonly busy = signal<number | null>(null);
  /** A Pass in flight (Fail shows its progress in the dialog). */
  protected readonly passing = signal<number | null>(null);

  protected readonly now = new Date();

  protected wait(deployedAt: string): { label: string; css: string } {
    const days = waitingDays(deployedAt, this.now);
    return { label: waitLabel(days), css: WAIT_CLASS[waitBucket(days)] };
  }

  protected setMine(mine: boolean): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { mine: mine ? 1 : null },
      replaceUrl: true,
    });
  }

  protected pass(group: RetestGroup, item: RetestItem): void {
    if (this.busy()) {
      return;
    }
    this.busy.set(item.deploymentId);
    this.passing.set(item.deploymentId);
    this.issues.retest(item.deploymentId, true).subscribe({
      next: (issue) =>
        this.done(`${item.issueKey}: retest passed on ${group.environmentName}.`, issue),
      error: (error: unknown) => this.failed(item, error),
    });
  }

  protected async fail(item: RetestItem): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(item.deploymentId);
    let issue: Issue;
    try {
      issue = await firstValueFrom(this.issues.get(item.issueKey));
    } catch (error: unknown) {
      this.failed(item, error);
      return;
    }
    const deployment = issue.deployments.find((d) => d.id === item.deploymentId);
    if (!deployment || deployment.retestResult !== 'Pending') {
      this.busy.set(null);
      this.toast.info(`${item.issueKey} was already retested.`);
      this.queue.reload();
      return;
    }
    const ref = this.dialog.open<ActionDialog, ActionDialogData, Issue>(ActionDialog, {
      data: {
        issue,
        action: { action: 'RetestFailed', deployment },
        assignees: [],
        environments: [],
      },
      width: '520px',
      maxWidth: 'calc(100vw - 32px)',
      restoreFocus: true,
    });
    const updated = await firstValueFrom(ref.afterClosed());
    this.busy.set(null);
    if (updated) {
      this.done(`${item.issueKey} reopened – back to ${updated.assigneeName ?? 'the developers'}.`);
    }
  }

  private done(message: string, issue?: Issue): void {
    this.passing.set(null);
    this.busy.set(null);
    this.toast.success(
      issue && issue.status !== 'Verified'
        ? `${message} Another retest is still pending.`
        : message,
    );
    this.queue.reload();
  }

  private failed(item: RetestItem, error: unknown): void {
    this.passing.set(null);
    this.busy.set(null);
    this.toast.error(`${item.issueKey}: ${problemMessage(error)}`);
    this.queue.reload();
  }
}
