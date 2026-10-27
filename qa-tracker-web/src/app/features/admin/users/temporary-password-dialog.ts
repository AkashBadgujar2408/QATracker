import { Clipboard } from '@angular/cdk/clipboard';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideCopy } from '@ng-icons/lucide';

export interface TemporaryPasswordData {
  readonly heading: string;
  readonly userName: string;
  readonly temporaryPassword: string;
}

/** Shows a temporary password once so the admin can hand it over. */
@Component({
  selector: 'app-temporary-password-dialog',
  imports: [MatDialogModule, MatButton, NgIcon],
  viewProviders: [provideIcons({ lucideCopy, lucideCheck })],
  template: `
    <h2 mat-dialog-title>{{ data.heading }}</h2>
    <mat-dialog-content>
      <p class="mt-0 text-sm text-text">
        Give these sign-in details to the user. They must choose their own password at first
        sign-in.
      </p>
      <dl
        class="m-0 grid grid-cols-[auto_1fr] items-center gap-x-4 gap-y-2 rounded-card bg-surface-muted p-4"
      >
        <dt class="text-sm text-muted">User name</dt>
        <dd class="m-0 font-mono text-base text-text">{{ data.userName }}</dd>
        <dt class="text-sm text-muted">Temporary password</dt>
        <dd class="m-0 flex items-center gap-2">
          <code class="font-mono text-base text-text select-all">{{ data.temporaryPassword }}</code>
          <button
            matButton
            type="button"
            (click)="copy()"
            [attr.aria-label]="'Copy sign-in details'"
          >
            <ng-icon [name]="copied() ? 'lucideCheck' : 'lucideCopy'" size="16" class="mr-1" />
            {{ copied() ? 'Copied' : 'Copy' }}
          </button>
        </dd>
      </dl>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton="filled" type="button" mat-dialog-close cdkFocusInitial>Done</button>
    </mat-dialog-actions>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TemporaryPasswordDialog {
  protected readonly data = inject<TemporaryPasswordData>(MAT_DIALOG_DATA);
  private readonly clipboard = inject(Clipboard);
  protected readonly copied = signal(false);

  protected copy(): void {
    this.copied.set(
      this.clipboard.copy(
        `User name: ${this.data.userName}\nTemporary password: ${this.data.temporaryPassword}`,
      ),
    );
  }
}
