import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatOptgroup, MatOption, MatSelect } from '@angular/material/select';
import { problemMessage } from '../../core/api/api';
import { ToastService } from '../../core/feedback/toast.service';
import { MastersApi, UserLookup } from '../../shared/data/masters.api';
import { MasterParent } from '../../shared/masters/master-dialog';
import { ROLES } from '../../shared/models/role';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { SkeletonLines } from '../../shared/ui/skeleton/skeleton';
import { UserAvatar } from '../../shared/ui/user-avatar/user-avatar';

/** Members tab: people who usually work on the project (told when testing completes – plan 8.3). */
@Component({
  selector: 'app-project-members',
  imports: [
    MatButton,
    MatFormField,
    MatLabel,
    MatSelect,
    MatOption,
    MatOptgroup,
    EmptyState,
    SkeletonLines,
    UserAvatar,
  ],
  template: `
    @if ((members.isLoading() && !members.hasValue()) || (users.isLoading() && !users.hasValue())) {
      <app-skeleton-lines [lines]="3" class="block p-4" />
    } @else if (members.error() || users.error()) {
      <app-empty-state kind="error" title="Could not load members" [message]="errorMessage()">
        <button matButton="outlined" type="button" (click)="reload()">Try again</button>
      </app-empty-state>
    } @else {
      <div class="px-4 pt-4">
        <mat-form-field appearance="outline" class="w-full" subscriptSizing="dynamic">
          <mat-label>Members</mat-label>
          <mat-select
            multiple
            [value]="selectedIds()"
            (valueChange)="draft.set($event)"
            (openedChange)="onOpenedChange($event)"
            [disabled]="saving()"
            placeholder="Choose people"
          >
            @for (group of userGroups(); track group.role) {
              <mat-optgroup [label]="group.role + 's'">
                @for (user of group.users; track user.id) {
                  <mat-option [value]="user.id">{{ user.fullName }}</mat-option>
                }
              </mat-optgroup>
            }
          </mat-select>
        </mat-form-field>
        <p class="mt-1 mb-0 text-sm text-muted">
          Members are notified when testing of a sprint is completed.
        </p>
      </div>
      @if (members.value()?.length === 0) {
        <app-empty-state
          title="No members yet"
          message="Choose the developers and testers who usually work on this project."
        />
      } @else {
        <ul class="m-0 mt-3 list-none border-t border-border p-0">
          @for (member of members.value(); track member.userId) {
            <li
              class="flex min-h-14 items-center gap-3 border-b border-border px-4 py-2 last:border-b-0"
              [class.opacity-60]="!member.isActive"
            >
              <app-user-avatar [name]="member.fullName" [color]="member.avatarColor" size="sm" />
              <div class="min-w-0 flex-1">
                <div class="truncate font-medium">{{ member.fullName }}</div>
                <div class="truncate text-xs text-muted">
                  {{ member.userName }}{{ member.isActive ? '' : ' (inactive user)' }}
                </div>
              </div>
              <span class="rounded-pill bg-surface-muted px-2.5 py-0.5 text-xs font-medium">{{
                member.role
              }}</span>
            </li>
          }
        </ul>
      }
    }
  `,
  host: { class: 'block pb-2' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProjectMembers {
  private readonly api = inject(MastersApi);
  private readonly toast = inject(ToastService);

  readonly project = input.required<MasterParent>();
  readonly changed = output<void>();

  protected readonly members = rxResource({
    params: () => this.project().id,
    stream: ({ params }) => this.api.members(params),
  });
  protected readonly users = rxResource({ stream: () => this.api.users() });

  /** The selection being edited; null = follow the saved members. */
  protected readonly draft = signal<number[] | null>(null);
  protected readonly saving = signal(false);

  protected readonly savedIds = computed(() => (this.members.value() ?? []).map((m) => m.userId));
  protected readonly selectedIds = computed(() => this.draft() ?? this.savedIds());

  protected readonly userGroups = computed(() => {
    const users = this.users.value() ?? [];
    return ROLES.map((role) => ({
      role,
      users: users.filter((u: UserLookup) => u.role === role),
    })).filter((group) => group.users.length > 0);
  });

  protected readonly errorMessage = computed(() =>
    problemMessage(this.members.error() ?? this.users.error()),
  );

  protected reload(): void {
    this.members.reload();
    this.users.reload();
  }

  /** Saves when the dropdown closes, if the selection changed. */
  protected onOpenedChange(open: boolean): void {
    if (!open) {
      this.save();
    }
  }

  private save(): void {
    const ids = this.draft();
    const saved = this.savedIds();
    if (!ids || (ids.length === saved.length && ids.every((id) => saved.includes(id)))) {
      this.draft.set(null);
      return;
    }
    this.saving.set(true);
    this.api.setMembers(this.project().id, ids).subscribe({
      next: (members) => {
        this.members.set(members);
        this.draft.set(null);
        this.saving.set(false);
        this.toast.success('Members saved.');
        this.changed.emit();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.toast.error(problemMessage(error));
      },
    });
  }
}
