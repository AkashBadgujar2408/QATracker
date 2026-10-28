import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { MasterKind } from '../data/masters.api';
import { PickerItem, QuickAdd } from '../ui/entity-picker/entity-picker';
import { MasterDialog, MasterDialogData, MasterDialogResult, MasterParent } from './master-dialog';

/** Opens the master create/edit dialog; also builds the quick-add callback for entity pickers. */
@Injectable({ providedIn: 'root' })
export class MasterDialogs {
  private readonly dialog = inject(MatDialog);

  async open(data: MasterDialogData): Promise<MasterDialogResult | null> {
    const ref = this.dialog.open<MasterDialog, MasterDialogData, MasterDialogResult>(MasterDialog, {
      data,
      width: '480px',
      maxWidth: 'calc(100vw - 32px)',
      restoreFocus: true,
    });
    return (await firstValueFrom(ref.afterClosed())) ?? null;
  }

  /**
   * Quick-add for a picker: `[quickAdd]="masterDialogs.quickAdd('Module', () => project())"`.
   * The parent is read when the user picks "+ Add", so it follows the cascade.
   */
  quickAdd(kind: MasterKind, parent?: () => MasterParent | null | undefined): QuickAdd {
    return async (name: string): Promise<PickerItem | null> => {
      const result = await this.open({ kind, name, parent: parent?.() ?? undefined });
      return result ? { id: result.id, label: result.name } : null;
    };
  }
}
