import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatIconButton } from '@angular/material/button';
import { MatDivider } from '@angular/material/divider';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltip } from '@angular/material/tooltip';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBellRing,
  lucideKeyboard,
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
import { KeyboardShortcuts } from '../../shortcuts/keyboard-shortcuts.service';
import { issueKeyFrom } from '../../shortcuts/shortcuts';

/**
 * Top bar: issue search (a key such as "ord-12" opens that issue, other text searches the issue list; "/" focuses it),
 * theme toggle, notifications bell and user menu (incl. keyboard shortcuts).
 */
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
      lucideKeyboard,
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
  protected readonly shortcuts = inject(KeyboardShortcuts);
  private readonly router = inject(Router);

  protected search(event: Event, input: HTMLInputElement): void {
    event.preventDefault();
    const text = input.value.trim();
    if (!text) {
      return;
    }
    const key = issueKeyFrom(text);
    void (key
      ? this.router.navigate(['/issues', key])
      : this.router.navigate(['/issues'], { queryParams: { q: text } }));
    input.value = '';
    input.blur();
  }
}
