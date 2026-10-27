import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormField, MatLabel, MatPrefix } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatOption, MatSelect } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideEllipsisVertical,
  lucideKeyRound,
  lucidePencil,
  lucidePlus,
  lucideSearch,
  lucideUserCheck,
  lucideUserX,
} from '@ng-icons/lucide';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { problemMessage } from '../../../core/api/api';
import { AuthService } from '../../../core/auth/auth.service';
import { ConfirmService } from '../../../core/feedback/confirm.service';
import { ToastService } from '../../../core/feedback/toast.service';
import { ROLES, Role } from '../../../shared/models/role';
import { IstDatePipe } from '../../../shared/pipes/ist-date.pipe';
import { EmptyState } from '../../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../../shared/ui/page-header/page-header';
import { SkeletonTable } from '../../../shared/ui/skeleton/skeleton';
import { UserAvatar } from '../../../shared/ui/user-avatar/user-avatar';
import { TemporaryPasswordData, TemporaryPasswordDialog } from './temporary-password-dialog';
import { UserDialog, UserDialogData, UserDialogResult } from './user-dialog';
import { User, UsersApi } from './users.api';

type StatusFilter = 'active' | 'inactive' | 'all';

/** Admin › Users (plan S05): list, create, edit, reset password, activate/deactivate. */
@Component({
  selector: 'app-users-page',
  imports: [
    MatTableModule,
    MatButton,
    MatIconButton,
    MatMenuModule,
    MatFormField,
    MatLabel,
    MatPrefix,
    MatInput,
    MatSelect,
    MatOption,
    NgIcon,
    PageHeader,
    EmptyState,
    SkeletonTable,
    UserAvatar,
    IstDatePipe,
  ],
  viewProviders: [
    provideIcons({
      lucideEllipsisVertical,
      lucideKeyRound,
      lucidePencil,
      lucidePlus,
      lucideSearch,
      lucideUserCheck,
      lucideUserX,
    }),
  ],
  templateUrl: './users-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersPage {
  private readonly api = inject(UsersApi);
  private readonly dialog = inject(MatDialog);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  protected readonly auth = inject(AuthService);

  protected readonly roles = ROLES;
  protected readonly columns = ['user', 'email', 'role', 'status', 'lastLogin', 'actions'];

  protected readonly search = signal('');
  protected readonly role = signal<Role | null>(null);
  protected readonly status = signal<StatusFilter>('active');
  private readonly debouncedSearch = toSignal(
    toObservable(this.search).pipe(debounceTime(250), distinctUntilChanged()),
    { initialValue: '' },
  );

  protected readonly users = rxResource({
    params: () => ({
      search: this.debouncedSearch(),
      role: this.role(),
      isActive: this.status() === 'all' ? null : this.status() === 'active',
    }),
    stream: ({ params }) => this.api.list(params),
  });

  protected readonly hasFilters = computed(
    () =>
      this.debouncedSearch().trim() !== '' || this.role() !== null || this.status() !== 'active',
  );
  protected readonly countLabel = computed(() => {
    if (!this.users.hasValue()) {
      return '';
    }
    const count = this.users.value().length;
    return count === 1 ? '1 user' : `${count} users`;
  });
  protected readonly errorMessage = computed(() =>
    this.users.error() ? problemMessage(this.users.error()) : null,
  );

  protected openCreate(): void {
    this.openDialog({});
  }

  protected openEdit(user: User): void {
    this.openDialog({ user });
  }

  protected async resetPassword(user: User): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Reset password for ${user.fullName}?`,
      message: 'A new temporary password is created and the user is signed out everywhere.',
      confirmLabel: 'Reset password',
    });
    if (!ok) {
      return;
    }
    this.api.resetPassword(user.id).subscribe((result) => {
      this.users.reload();
      this.showTemporaryPassword({
        heading: 'Password reset',
        userName: result.user.userName,
        temporaryPassword: result.temporaryPassword,
      });
    });
  }

  protected async setActive(user: User, isActive: boolean): Promise<void> {
    if (!isActive) {
      const ok = await this.confirm.ask({
        title: `Deactivate ${user.fullName}?`,
        message:
          'They are signed out immediately and cannot sign in until reactivated. History stays intact.',
        confirmLabel: 'Deactivate',
        danger: true,
      });
      if (!ok) {
        return;
      }
    }
    this.api.setActive(user.id, isActive).subscribe({
      next: () => {
        this.toast.success(`${user.fullName} ${isActive ? 'activated' : 'deactivated'}.`);
        this.users.reload();
      },
      error: (error: unknown) => this.toast.error(problemMessage(error)),
    });
  }

  protected clearFilters(): void {
    this.search.set('');
    this.role.set(null);
    this.status.set('active');
  }

  private openDialog(data: UserDialogData): void {
    this.dialog
      .open<UserDialog, UserDialogData, UserDialogResult>(UserDialog, {
        data,
        width: '520px',
        maxWidth: 'calc(100vw - 32px)',
      })
      .afterClosed()
      .subscribe((result) => {
        if (!result) {
          return;
        }
        this.users.reload();
        if ('created' in result) {
          this.showTemporaryPassword({
            heading: 'User created',
            userName: result.created.user.userName,
            temporaryPassword: result.created.temporaryPassword,
          });
        } else {
          this.toast.success(`${result.updated.fullName} saved.`);
        }
      });
  }

  private showTemporaryPassword(data: TemporaryPasswordData): void {
    this.dialog.open(TemporaryPasswordDialog, {
      data,
      width: '480px',
      maxWidth: 'calc(100vw - 32px)',
    });
  }
}
