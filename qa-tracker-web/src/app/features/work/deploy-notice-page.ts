import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatCheckbox } from '@angular/material/checkbox';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect, MatSelectTrigger } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCircleCheck, lucideCircleX, lucideClock } from '@ng-icons/lucide';
import { catchError, map, of } from 'rxjs';
import { problemMessage, problemOf } from '../../core/api/api';
import { ToastService } from '../../core/feedback/toast.service';
import { MastersApi } from '../../shared/data/masters.api';
import { DeployCandidate, WorkApi } from '../../shared/data/work.api';
import { IstDatePipe } from '../../shared/pipes/ist-date.pipe';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';
import { WorkItemRow } from './work-item-row';

export interface CandidateGroup {
  readonly projectId: number;
  readonly label: string;
  readonly candidates: readonly DeployCandidate[];
}

/** Candidates per project, in the API's order. */
export function groupByProject(candidates: readonly DeployCandidate[]): CandidateGroup[] {
  const groups = new Map<number, CandidateGroup>();
  for (const c of candidates) {
    const group = groups.get(c.issue.projectId) ?? {
      projectId: c.issue.projectId,
      label: `${c.issue.clientName} › ${c.issue.projectName} (${c.issue.projectKey})`,
      candidates: [],
    };
    groups.set(c.issue.projectId, { ...group, candidates: [...group.candidates, c] });
  }
  return [...groups.values()];
}

/** Why a candidate cannot go into the notice now, or null. */
export function blockedReason(
  candidate: DeployCandidate,
  projectId: number | null,
  environment: { id: number; name: string } | null,
): string | null {
  if (projectId !== null && candidate.issue.projectId !== projectId) {
    return 'Another project – send a separate notice.';
  }
  if (
    environment &&
    candidate.deployments.some(
      (d) => d.environmentId === environment.id && d.retestResult === 'Pending',
    )
  ) {
    return `Already waiting for a retest on ${environment.name}.`;
  }
  return null;
}

/**
 * Deploy notice (plan 7.4, 8.2): select your fixed issues, the environment they went to and one note – each issue
 * gets a pending retest and a timeline entry; the tester finds them in the retest queue.
 */
@Component({
  selector: 'app-deploy-notice-page',
  imports: [
    FormsModule,
    MatButton,
    MatCheckbox,
    MatFormField,
    MatLabel,
    MatInput,
    MatSelect,
    MatOption,
    MatSelectTrigger,
    RouterLink,
    NgIcon,
    IstDatePipe,
    EmptyState,
    PageHeader,
    SkeletonLines,
    WorkItemRow,
  ],
  viewProviders: [provideIcons({ lucideCircleCheck, lucideCircleX, lucideClock })],
  templateUrl: './deploy-notice-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DeployNoticePage {
  private readonly workApi = inject(WorkApi);
  private readonly masters = inject(MastersApi);
  private readonly toast = inject(ToastService);

  protected readonly candidates = rxResource({ stream: () => this.workApi.deployCandidates() });
  protected readonly recent = rxResource({
    stream: () => this.workApi.deployNotices(null, 5).pipe(catchError(() => of([]))),
  });
  protected readonly groups = computed(() =>
    groupByProject(this.candidates.hasValue() ? this.candidates.value() : []),
  );

  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly environmentId = signal<number | null>(null);
  protected note = '';
  protected version = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  /** The project of the selection (a notice covers one project). */
  protected readonly projectId = computed(() => {
    const keys = this.selected();
    const first = (this.candidates.value() ?? []).find((c) => keys.has(c.issue.key));
    return first?.issue.projectId ?? null;
  });
  protected readonly environments = rxResource({
    params: () => this.projectId() ?? undefined,
    stream: ({ params }) =>
      this.masters.cascade(null, params).pipe(
        map((c) => c.environments),
        catchError(() => of([])),
      ),
  });
  protected readonly environment = computed(() => {
    const id = this.environmentId();
    const env = (this.environments.value() ?? []).find((e) => e.id === id);
    return env ? { id: env.id, name: env.name } : null;
  });

  constructor() {
    // Another project (or none) → the chosen environment no longer applies.
    effect(() => {
      const environments = this.environments.value() ?? [];
      const id = this.environmentId();
      if (id !== null && !environments.some((e) => e.id === id)) {
        this.environmentId.set(null);
      }
    });
    // Choosing an environment drops issues that already wait for a retest there.
    effect(() => {
      const env = this.environment();
      const keys = this.selected();
      if (!env || keys.size === 0) {
        return;
      }
      const keep = (this.candidates.value() ?? [])
        .filter((c) => keys.has(c.issue.key) && !blockedReason(c, null, env))
        .map((c) => c.issue.key);
      if (keep.length !== keys.size) {
        this.selected.set(new Set(keep));
      }
    });
  }

  protected blocked(candidate: DeployCandidate): string | null {
    return blockedReason(candidate, this.projectId(), this.environment());
  }

  protected toggle(key: string, checked: boolean): void {
    const next = new Set(this.selected());
    if (checked) {
      next.add(key);
    } else {
      next.delete(key);
    }
    this.selected.set(next);
    this.error.set(null);
  }

  /** Select every issue of the project that can go to the chosen environment. */
  protected selectAll(group: CandidateGroup): void {
    const env = this.environment();
    const keys = group.candidates
      .filter((c) => !blockedReason(c, group.projectId, env))
      .map((c) => c.issue.key);
    this.selected.set(new Set(keys));
    this.error.set(null);
  }

  protected send(): void {
    if (this.busy()) {
      return;
    }
    const env = this.environment();
    const note = this.note.trim();
    const problem =
      this.selected().size === 0
        ? 'Select at least one fixed issue.'
        : !env
          ? 'Choose the environment the fixes are deployed on.'
          : !note
            ? 'Write a note for the tester.'
            : null;
    if (problem || !env) {
      this.error.set(problem);
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.workApi
      .createDeployNotice({
        environmentId: env.id,
        issueKeys: [...this.selected()],
        note,
        version: this.version.trim() || null,
      })
      .subscribe({
        next: (notice) => {
          this.busy.set(false);
          const n = notice.issues.length;
          this.toast.success(
            `Deploy notice sent: ${n} ${n === 1 ? 'issue' : 'issues'} on ${notice.environmentName}. They are in the tester's retest queue.`,
          );
          this.selected.set(new Set());
          this.note = '';
          this.version = '';
          this.candidates.reload();
          this.recent.reload();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          const p = problemOf(error);
          this.error.set(
            p?.errors ? Object.values(p.errors).flat().join(' ') : problemMessage(error),
          );
          if (p?.status === 422) {
            this.candidates.reload();
          }
        },
      });
  }
}
