import {
  ImportCorrections,
  ImportPreview,
  ImportRow,
  RowCorrection,
} from '../../shared/data/import.api';
import { Priority, Severity } from '../../shared/models/issue-enums';

/** What the reviewer changed in a row of the preview grid. */
export interface RowEdit {
  readonly include: boolean;
  readonly title: string;
  readonly severity: Severity;
  readonly priority: Priority;
  readonly assigneeId: number | null;
}

export function initialEdit(row: ImportRow): RowEdit {
  return {
    include: true,
    title: row.title,
    severity: row.severity,
    priority: row.priority,
    assigneeId: row.assigneeId,
  };
}

/**
 * The warnings a row still has after the reviewer's edits – an edited field settles its warning (the API does the
 * same on commit, ADR-036).
 */
export function remainingWarnings(row: ImportRow, edit: RowEdit): string[] {
  return row.warnings.filter((w) => {
    if (w.startsWith('Title')) {
      return edit.title.trim() === row.title;
    }
    if (w.startsWith('Severity')) {
      return edit.severity === row.severity;
    }
    if (w.startsWith('Priority')) {
      return edit.priority === row.priority;
    }
    if (w.startsWith('Resolve By')) {
      return edit.assigneeId === row.assigneeId;
    }
    return true;
  });
}

/** Only what changed is sent: skipped rows, edited fields, renamed modules. */
export function buildCorrections(
  preview: ImportPreview,
  edits: ReadonlyMap<string, RowEdit>,
  moduleNames: ReadonlyMap<number, string>,
): ImportCorrections {
  const rows: RowCorrection[] = [];
  for (const row of preview.rows) {
    const edit = edits.get(row.id) ?? initialEdit(row);
    if (!edit.include) {
      rows.push({ id: row.id, skip: true });
      continue;
    }
    const title = edit.title.trim();
    const correction: {
      -readonly [K in keyof RowCorrection]: RowCorrection[K];
    } = { id: row.id };
    if (title && title !== row.title) {
      correction.title = title;
    }
    if (edit.severity !== row.severity) {
      correction.severity = edit.severity;
    }
    if (edit.priority !== row.priority) {
      correction.priority = edit.priority;
    }
    if (edit.assigneeId !== row.assigneeId) {
      if (edit.assigneeId === null) {
        correction.unassign = true;
      } else {
        correction.assigneeId = edit.assigneeId;
      }
    }
    if (Object.keys(correction).length > 1) {
      rows.push(correction);
    }
  }

  const modules = preview.modules
    .map((m) => ({
      sheetIndex: m.sheetIndex,
      name: (moduleNames.get(m.sheetIndex) ?? m.name).trim(),
    }))
    .filter((m, i) => m.name.length > 0 && m.name !== preview.modules[i].name);

  return { modules, rows };
}
