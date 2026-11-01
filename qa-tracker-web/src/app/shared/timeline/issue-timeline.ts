import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, debounceTime, filter } from 'rxjs';
import { MatButton } from '@angular/material/button';
import { MatButtonToggle, MatButtonToggleGroup } from '@angular/material/button-toggle';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowRight,
  lucideCircleCheck,
  lucideCircleX,
  lucideFilePlus,
  lucideFlag,
  lucideMessageSquare,
  lucidePaperclip,
  lucidePencil,
  lucideRefreshCw,
  lucideRocket,
  lucideUserPlus,
} from '@ng-icons/lucide';
import { problemMessage } from '../../core/api/api';
import { RealtimeService } from '../../core/realtime/realtime.service';
import { UserLookup } from '../data/masters.api';
import { Activity, ActivityKind, Mention, TimelineApi, TimelineFilter } from '../data/timeline.api';
import { IstDatePipe, relativeTime } from '../pipes/ist-date.pipe';
import { AttachmentGallery } from '../ui/attachment-gallery/attachment-gallery';
import { SkeletonLines } from '../ui/skeleton/skeleton';
import { UserAvatar } from '../ui/user-avatar/user-avatar';
import { CommentComposer } from './comment-composer';

export interface Segment {
  readonly text: string;
  readonly mention: boolean;
}

/** Splits a comment into text and @mention pieces (only names that really were mentioned are highlighted). */
export function commentSegments(body: string, mentions: readonly Mention[]): Segment[] {
  const names = new Set(mentions.map((m) => m.userName.toLowerCase()));
  const segments: Segment[] = [];
  let last = 0;
  for (const match of body.matchAll(/@([A-Za-z0-9._-]{3,50})/g)) {
    const name = match[1].replace(/[._-]+$/, '');
    if (!names.has(name.toLowerCase())) {
      continue;
    }
    if (match.index > last) {
      segments.push({ text: body.slice(last, match.index), mention: false });
    }
    segments.push({ text: `@${name}`, mention: true });
    last = match.index + name.length + 1;
  }
  if (last < body.length) {
    segments.push({ text: body.slice(last), mention: false });
  }
  return segments;
}

export { relativeTime } from '../pipes/ist-date.pipe';

const ICONS: Readonly<Record<ActivityKind, string>> = {
  Created: 'lucideFilePlus',
  Comment: 'lucideMessageSquare',
  StatusChanged: 'lucideRefreshCw',
  Assigned: 'lucideUserPlus',
  FieldChanged: 'lucidePencil',
  Deployed: 'lucideRocket',
  RetestPassed: 'lucideCircleCheck',
  RetestFailed: 'lucideCircleX',
  Attachment: 'lucidePaperclip',
  CarriedForward: 'lucideArrowRight',
  SprintEvent: 'lucideFlag',
};

/** Events whose body is a note worth showing under the line (reason, fix note, retest comment). */
const NOTE_KINDS: ReadonlySet<ActivityKind> = new Set([
  'StatusChanged',
  'Assigned',
  'Deployed',
  'RetestPassed',
  'RetestFailed',
  'CarriedForward',
]);

/**
 * One stream of comments and system events per issue (plan 7.5), oldest first, with "Show older", the
 * All · Comments · History filter, inline edit of one's own comments (10 minutes) and the composer.
 */
@Component({
  selector: 'app-issue-timeline',
  imports: [
    MatButton,
    MatButtonToggle,
    MatButtonToggleGroup,
    NgIcon,
    IstDatePipe,
    AttachmentGallery,
    SkeletonLines,
    UserAvatar,
    CommentComposer,
  ],
  viewProviders: [
    provideIcons({
      lucideArrowRight,
      lucideCircleCheck,
      lucideCircleX,
      lucideFilePlus,
      lucideFlag,
      lucideMessageSquare,
      lucidePaperclip,
      lucidePencil,
      lucideRefreshCw,
      lucideRocket,
      lucideUserPlus,
    }),
  ],
  template: `
    <div class="mb-4 flex flex-wrap items-center gap-3">
      <h2 class="m-0 flex-1 text-md font-semibold">Activity</h2>
      <mat-button-toggle-group
        [value]="filter()"
        (change)="setFilter($event.value)"
        aria-label="Show"
        hideSingleSelectionIndicator
        class="qa-filter"
      >
        <mat-button-toggle value="All">All</mat-button-toggle>
        <mat-button-toggle value="Comments">Comments</mat-button-toggle>
        <mat-button-toggle value="History">History</mat-button-toggle>
      </mat-button-toggle-group>
    </div>

    @if (hasMore()) {
      <button matButton type="button" class="mb-2" [disabled]="loading()" (click)="loadOlder()">
        Show older activity
      </button>
    }

    @if (loading() && !items().length) {
      <app-skeleton-lines [lines]="5" />
    } @else if (error()) {
      <p role="alert" class="m-0 text-sm text-danger">
        Could not load the activity. {{ error() }}
        <button matButton type="button" (click)="reload()">Try again</button>
      </p>
    } @else if (!items().length) {
      <p class="m-0 text-sm text-muted">
        {{ filter() === 'Comments' ? 'No comments yet.' : 'Nothing here yet.' }}
      </p>
    } @else {
      <ol class="qa-timeline m-0 list-none p-0" aria-live="polite">
        @for (item of items(); track item.id) {
          <li class="relative flex gap-3 pb-4">
            @if (item.kind === 'Comment') {
              <app-user-avatar
                [name]="item.userName ?? 'System'"
                [color]="item.avatarColor"
                size="sm"
                class="relative z-[1] mt-0.5"
              />
              <div class="min-w-0 flex-1 rounded-card border border-border bg-surface px-3 py-2">
                <div class="flex flex-wrap items-baseline gap-x-2 text-sm">
                  <span class="font-semibold text-text">{{ item.userName }}</span>
                  <time class="text-muted" [title]="item.createdAt | istDate: 'datetime'">{{
                    ago(item.createdAt)
                  }}</time>
                  @if (item.editedAt) {
                    <span class="text-xs text-muted">(edited)</span>
                  }
                  @if (item.canEdit && editingId() !== item.id) {
                    <button
                      type="button"
                      class="ml-auto cursor-pointer border-0 bg-transparent p-0 text-xs text-primary hover:underline"
                      (click)="startEdit(item)"
                    >
                      Edit
                    </button>
                  }
                </div>
                @if (editingId() === item.id) {
                  <label class="sr-only" [for]="'qa-edit-' + item.id">Edit comment</label>
                  <textarea
                    [id]="'qa-edit-' + item.id"
                    rows="3"
                    class="mt-1 block w-full resize-y rounded-control border border-border-strong bg-surface px-2 py-1.5 text-base text-text"
                    [value]="editText()"
                    (input)="editText.set($any($event.target).value)"
                    (keydown.control.enter)="saveEdit(item)"
                    (keydown.escape)="editingId.set(null)"
                  ></textarea>
                  @if (editError()) {
                    <p role="alert" class="m-0 mt-1 text-sm text-danger">
                      {{ editError() }}
                    </p>
                  }
                  <div class="mt-1 flex justify-end gap-2">
                    <button matButton type="button" (click)="editingId.set(null)">Cancel</button>
                    <button matButton="filled" type="button" (click)="saveEdit(item)">Save</button>
                  </div>
                } @else if (item.body) {
                  <p class="m-0 mt-1 text-base break-words whitespace-pre-wrap text-text">
                    @for (part of segments(item); track $index) {
                      @if (part.mention) {
                        <span class="rounded-[4px] bg-primary/10 px-0.5 font-medium text-primary">{{
                          part.text
                        }}</span>
                      } @else {
                        {{ part.text }}
                      }
                    }
                  </p>
                }
                @if (item.attachments.length) {
                  <app-attachment-gallery [attachments]="item.attachments" class="mt-2 block" />
                }
              </div>
            } @else {
              <span
                class="relative z-[1] flex size-6 shrink-0 items-center justify-center rounded-full border border-border bg-surface"
                [class]="eventTone(item.kind)"
                aria-hidden="true"
              >
                <ng-icon [name]="icon(item.kind)" size="13" />
              </span>
              <div class="min-w-0 flex-1 pt-0.5">
                <div class="flex flex-wrap items-baseline gap-x-2 text-sm">
                  <span class="text-text">{{ item.summary }}</span>
                  <time class="text-muted" [title]="item.createdAt | istDate: 'datetime'">{{
                    ago(item.createdAt)
                  }}</time>
                </div>
                @if (showNote(item)) {
                  <p
                    class="m-0 mt-1 border-l-2 border-border-strong pl-2 text-sm break-words whitespace-pre-wrap text-muted"
                  >
                    {{ item.body }}
                  </p>
                }
              </div>
            }
          </li>
        }
      </ol>
    }

    <app-comment-composer
      class="mt-2"
      [issueKey]="issueKey()"
      [users]="users()"
      (posted)="onPosted($event)"
    />
  `,
  styles: `
    .qa-timeline > li:not(:last-child)::before {
      content: '';
      position: absolute;
      left: 11.5px;
      top: 1.5rem;
      bottom: 0;
      width: 1px;
      background: var(--qa-border);
    }
    .qa-filter {
      --mat-button-toggle-height: 30px;
      --mat-button-toggle-label-text-size: var(--qa-text-sm);
      --mat-button-toggle-shape: var(--qa-radius-control);
      --mat-button-toggle-selected-state-background-color: color-mix(
        in srgb,
        var(--qa-primary) 14%,
        transparent
      );
    }
  `,
  host: { class: 'block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IssueTimeline {
  readonly issueKey = input.required<string>();
  /** People who can be @mentioned. */
  readonly users = input<readonly UserLookup[]>([]);

  private readonly api = inject(TimelineApi);
  private readonly realtime = inject(RealtimeService);
  private readonly composer = viewChild.required(CommentComposer);

  protected readonly filter = signal<TimelineFilter>('All');
  protected readonly items = signal<readonly Activity[]>([]);
  protected readonly hasMore = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly editingId = signal<number | null>(null);
  protected readonly editText = signal('');
  protected readonly editError = signal<string | null>(null);
  /** Re-render relative times on every load. */
  private readonly now = signal(new Date());

  private readonly segmentCache = computed(() => {
    const map = new Map<number, Segment[]>();
    for (const item of this.items()) {
      if (item.kind === 'Comment' && item.body) {
        map.set(item.id, commentSegments(item.body, item.mentions));
      }
    }
    return map;
  });

  constructor() {
    effect(() => {
      this.issueKey();
      this.filter();
      untracked(() => this.reload());
    });
    // Live: join the issue's group while shown; new or edited entries by others reload the newest page.
    effect((onCleanup) => {
      const key = this.issueKey();
      onCleanup(untracked(() => this.realtime.watchIssue(key)));
    });
    const live = new Subject<void>();
    this.realtime.activityAdded$
      .pipe(
        filter((e) => e.issueKey === this.issueKey().trim().toUpperCase()),
        takeUntilDestroyed(),
      )
      .subscribe(() => live.next());
    this.realtime.reconnected$.pipe(takeUntilDestroyed()).subscribe(() => live.next());
    live.pipe(debounceTime(300), takeUntilDestroyed()).subscribe(() => this.refreshLive());
  }

  /** Live reload, unless a comment is being edited (that would drop the text). Keeps older pages loaded. */
  private refreshLive(): void {
    if (this.editingId() !== null || this.loading()) {
      return;
    }
    if (this.items().length > 0 && this.hasMore()) {
      this.mergeNewest();
      return;
    }
    this.reload();
  }

  /** Replace the newest page in place so "Show older" pages stay. */
  private mergeNewest(): void {
    this.api.activities(this.issueKey(), this.filter()).subscribe({
      next: (page) => {
        this.now.set(new Date());
        const firstNew = page.items[0]?.id ?? Number.MAX_SAFE_INTEGER;
        this.items.update((items) => [...items.filter((i) => i.id < firstNew), ...page.items]);
      },
      error: () => undefined,
    });
  }

  /** Reload the newest page (after a workflow action). */
  reload(): void {
    this.load(undefined, false);
  }

  focusComposer(): void {
    this.composer().focus();
  }

  protected setFilter(filter: TimelineFilter): void {
    this.filter.set(filter);
  }

  protected loadOlder(): void {
    this.load(this.items()[0]?.id, true);
  }

  protected icon(kind: ActivityKind): string {
    return ICONS[kind];
  }

  protected eventTone(kind: ActivityKind): string {
    return kind === 'RetestPassed'
      ? 'text-status-verified'
      : kind === 'RetestFailed'
        ? 'text-status-reopened'
        : kind === 'Deployed'
          ? 'text-status-fixed'
          : 'text-muted';
  }

  protected showNote(item: Activity): boolean {
    return !!item.body && NOTE_KINDS.has(item.kind);
  }

  protected segments(item: Activity): Segment[] {
    return this.segmentCache().get(item.id) ?? [];
  }

  protected ago(iso: string): string {
    return relativeTime(iso, this.now());
  }

  protected startEdit(item: Activity): void {
    this.editingId.set(item.id);
    this.editText.set(item.body ?? '');
    this.editError.set(null);
  }

  protected saveEdit(item: Activity): void {
    const body = this.editText().trim();
    if (!body) {
      this.editError.set('A comment cannot be empty.');
      return;
    }
    this.api.editComment(item.id, body).subscribe({
      next: (updated) => {
        this.editingId.set(null);
        this.items.update((items) => items.map((i) => (i.id === updated.id ? updated : i)));
      },
      error: (error: unknown) => this.editError.set(problemMessage(error)),
    });
  }

  protected onPosted(comment: Activity): void {
    if (this.filter() !== 'History') {
      this.items.update((items) => [...items, comment]);
    }
  }

  private load(before: number | undefined, older: boolean): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.activities(this.issueKey(), this.filter(), before).subscribe({
      next: (page) => {
        this.loading.set(false);
        this.now.set(new Date());
        this.items.update((items) => (older ? [...page.items, ...items] : page.items));
        this.hasMore.set(page.hasMore);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(problemMessage(error));
      },
    });
  }
}
