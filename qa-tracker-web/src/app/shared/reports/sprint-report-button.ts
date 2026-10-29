import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideDownload, lucideFileSpreadsheet, lucideFileText } from '@ng-icons/lucide';
import { ToastService } from '../../core/feedback/toast.service';
import { saveFile } from '../data/download';
import { SprintsApi } from '../data/sprints.api';

/**
 * "Download report" with Excel (summary, a sheet per module like the legacy workbook, all issues) or PDF (cover,
 * summary, issues with screenshots) – the sheet that is emailed to clients (plan 7).
 */
@Component({
  selector: 'app-sprint-report-button',
  imports: [MatButton, MatMenu, MatMenuItem, MatMenuTrigger, NgIcon],
  viewProviders: [provideIcons({ lucideDownload, lucideFileSpreadsheet, lucideFileText })],
  template: `
    <button
      matButton="outlined"
      type="button"
      [matMenuTriggerFor]="formats"
      [disabled]="busy() !== null"
      [attr.aria-busy]="busy() !== null"
    >
      <ng-icon name="lucideDownload" size="16" class="mr-1" />
      {{ busy() ? 'Preparing ' + (busy() === 'pdf' ? 'PDF' : 'Excel') + '…' : 'Download report' }}
    </button>
    <mat-menu #formats="matMenu" xPosition="before">
      <button mat-menu-item type="button" (click)="download('xlsx')">
        <ng-icon name="lucideFileSpreadsheet" size="16" class="mr-2 align-middle" />
        <span>Excel workbook (.xlsx)</span>
      </button>
      <button mat-menu-item type="button" (click)="download('pdf')">
        <ng-icon name="lucideFileText" size="16" class="mr-2 align-middle" />
        <span>PDF with screenshots</span>
      </button>
    </mat-menu>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SprintReportButton {
  private readonly api = inject(SprintsApi);
  private readonly toast = inject(ToastService);

  readonly sprintId = input.required<number>();
  protected readonly busy = signal<'xlsx' | 'pdf' | null>(null);

  protected download(format: 'xlsx' | 'pdf'): void {
    this.busy.set(format);
    this.api.report(this.sprintId(), format).subscribe({
      next: (file) => {
        saveFile(file);
        this.busy.set(null);
      },
      error: () => {
        this.busy.set(null);
        this.toast.error('The report could not be created. Try again in a moment.');
      },
    });
  }
}
