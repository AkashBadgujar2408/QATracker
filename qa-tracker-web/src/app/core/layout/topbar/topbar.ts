import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconButton } from '@angular/material/button';
import { MatDivider } from '@angular/material/divider';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltip } from '@angular/material/tooltip';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBellRing,
  lucideKeyRound,
  lucideLogOut,
  lucideMoon,
  lucideSearch,
  lucideSun,
} from '@ng-icons/lucide';
import { UserAvatar } from '../../../shared/ui/user-avatar/user-avatar';
import { AuthService } from '../../auth/auth.service';
import { NotificationBell } from '../../notifications/notification-bell';
import { ThemeService } from '../../theme/theme.service';

/** Top bar: global search (placeholder until S15), theme toggle, notifications bell and user menu. */
@Component({
  selector: 'app-topbar',
  imports: [
    MatIconButton,
    MatMenuModule,
    MatDivider,
    MatTooltip,
    NgIcon,
    UserAvatar,
    RouterLink,
    NotificationBell,
  ],
  viewProviders: [
    provideIcons({
      lucideBellRing,
      lucideKeyRound,
      lucideLogOut,
      lucideMoon,
      lucideSearch,
      lucideSun,
    }),
  ],
  templateUrl: './topbar.html',
  host: {
    class: 'flex shrink-0 items-center gap-3 border-b border-border bg-surface px-4',
    '[style.height]': `'var(--qa-topbar-height)'`,
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Topbar {
  protected readonly theme = inject(ThemeService);

  protected readonly auth = inject(AuthService);
}
