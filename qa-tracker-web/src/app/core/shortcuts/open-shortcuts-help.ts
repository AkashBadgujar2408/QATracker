import { Injector } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import {
  ShortcutsDialog,
  ShortcutsDialogData,
} from '../../shared/ui/shortcuts-dialog/shortcuts-dialog';
import { ShortcutGroup } from './shortcuts';

/** Loaded on the first "?" only – keeps MatDialog out of the initial bundle (the shell imports no dialog). */
export function openShortcutsHelp(injector: Injector, groups: readonly ShortcutGroup[]): void {
  const dialog = injector.get(MatDialog);
  if (dialog.getDialogById('qa-shortcuts')) {
    return;
  }
  dialog.open<ShortcutsDialog, ShortcutsDialogData>(ShortcutsDialog, {
    id: 'qa-shortcuts',
    width: '720px',
    maxWidth: 'calc(100vw - 32px)',
    autoFocus: 'dialog',
    data: { groups },
  });
}
