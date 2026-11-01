import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { ShortcutGroup } from '../../../core/shortcuts/shortcuts';

export interface ShortcutsDialogData {
  readonly groups: readonly ShortcutGroup[];
}

/**
 * Keyboard shortcuts help (press "?" anywhere, or user menu › Keyboard shortcuts). Lists the keys for the signed-in
 * role in two columns; each key is a <kbd>, keys pressed one after the other are joined by "then".
 */
@Component({
  selector: 'app-shortcuts-dialog',
  imports: [MatDialogModule, MatButton],
  template: `
    <h2 mat-dialog-title>Keyboard shortcuts</h2>
    <mat-dialog-content>
      <div class="grid gap-x-10 gap-y-6 sm:grid-cols-2">
        @for (group of data.groups; track group.title) {
          <section [attr.aria-labelledby]="'qa-shortcuts-' + $index">
            <h3 [id]="'qa-shortcuts-' + $index" class="m-0 mb-2 text-sm font-semibold text-muted">
              {{ group.title }}
            </h3>
            <dl class="m-0 grid grid-cols-[auto_1fr] items-baseline gap-x-4 gap-y-2">
              @for (shortcut of group.shortcuts; track shortcut.description) {
                <dt class="flex items-center gap-1 whitespace-nowrap">
                  @for (key of shortcut.keys; track $index) {
                    @if (!$first) {
                      <span class="text-xs text-muted">{{ chord(shortcut) ? 'then' : '/' }}</span>
                    }
                    <kbd class="qa-kbd">{{ key }}</kbd>
                  }
                </dt>
                <dd class="m-0 text-base text-text">{{ shortcut.description }}</dd>
              }
            </dl>
          </section>
        }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton="filled" mat-dialog-close>Close</button>
    </mat-dialog-actions>
  `,
  styles: `
    .qa-kbd {
      display: inline-flex;
      min-width: 1.75rem;
      height: 1.75rem;
      align-items: center;
      justify-content: center;
      padding: 0 0.4rem;
      border: 1px solid var(--qa-border);
      border-bottom-width: 2px;
      border-radius: var(--qa-radius-control);
      background: var(--qa-surface-muted);
      color: var(--qa-text);
      font: inherit;
      font-size: 0.8125rem;
      font-weight: 600;
      line-height: 1;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShortcutsDialog {
  protected readonly data = inject<ShortcutsDialogData>(MAT_DIALOG_DATA);

  /** "g" then "i" is a sequence; "←" / "→" are alternatives. */
  protected chord(shortcut: { keys: readonly string[] }): boolean {
    return shortcut.keys[0] === 'g';
  }
}
