import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL, SILENT_ERRORS } from '../../core/api/api';
import { IssueStatus, Priority, Severity } from '../models/issue-enums';
import { RejectType } from './issues.api';

/** A workbook sheet and the module its issues go to (ADR-036). */
export interface ImportModule {
  readonly sheetIndex: number;
  readonly sheet: string;
  readonly name: string;
  /** Set when the sheet matches an existing module of the project. */
  readonly existingModuleId: number | null;
  readonly issues: number;
}

/** One "N. Title" block as it will be created. No warnings = needs no manual correction. */
export interface ImportRow {
  /** "sheet-row", e.g. "1-14". */
  readonly id: string;
  readonly sheetIndex: number;
  readonly sheet: string;
  readonly row: number;
  readonly module: string;
  readonly title: string;
  readonly steps: string | null;
  readonly actualResult: string;
  readonly expectedResult: string | null;
  readonly impact: string | null;
  readonly severity: Severity;
  readonly priority: Priority;
  readonly assigneeId: number | null;
  readonly assigneeName: string | null;
  /** "Resolve By" as written in the workbook. */
  readonly resolveBy: string | null;
  readonly status: IssueStatus;
  readonly holdReason: string | null;
  readonly rejectType: RejectType | null;
  readonly comments: readonly string[];
  readonly images: number;
  readonly warnings: readonly string[];
}

export interface ImportPreview {
  readonly sprintId: number;
  readonly sprintName: string;
  readonly projectKey: string;
  readonly modules: readonly ImportModule[];
  readonly rows: readonly ImportRow[];
  readonly readyRows: number;
  readonly warnings: readonly string[];
}

export interface RowCorrection {
  readonly id: string;
  readonly skip?: boolean;
  readonly title?: string;
  readonly severity?: Severity;
  readonly priority?: Priority;
  readonly assigneeId?: number;
  readonly unassign?: boolean;
}

export interface ImportCorrections {
  readonly modules: readonly { readonly sheetIndex: number; readonly name: string }[];
  readonly rows: readonly RowCorrection[];
}

export interface ImportResult {
  readonly created: number;
  readonly skipped: number;
  readonly modulesCreated: number;
  readonly attachments: number;
  readonly firstKey: string | null;
  readonly lastKey: string | null;
  readonly warnings: readonly string[];
}

/** The wizard shows problems inline. */
const inline = { context: new HttpContext().set(SILENT_ERRORS, true) };

@Injectable({ providedIn: 'root' })
export class ImportApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL);

  preview(sprintId: number, workbook: File): Observable<ImportPreview> {
    return this.http.post<ImportPreview>(
      `${this.base}/import/excel/preview`,
      form(sprintId, workbook),
      inline,
    );
  }

  commit(
    sprintId: number,
    workbook: File,
    corrections: ImportCorrections,
  ): Observable<ImportResult> {
    const body = form(sprintId, workbook);
    body.append('corrections', JSON.stringify(corrections));
    return this.http.post<ImportResult>(`${this.base}/import/excel/commit`, body, inline);
  }
}

function form(sprintId: number, workbook: File): FormData {
  const body = new FormData();
  body.append('file', workbook, workbook.name);
  body.append('sprintId', String(sprintId));
  return body;
}
