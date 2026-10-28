import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogClose } from '@angular/material/dialog';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideChevronRight, lucideDownload, lucideX } from '@ng-icons/lucide';
import { Attachment } from '../../data/attachments.api';

export interface LightboxData {
  readonly images: readonly Attachment[];
  readonly index: number;
}

/** Full-size image viewer: ← / → to move, Esc to close (MatDialog). */
@Component({
  selector: 'app-attachment-lightbox',
  imports: [MatIconButton, MatDialogClose, NgIcon],
  viewProviders: [provideIcons({ lucideChevronLeft, lucideChevronRight, lucideDownload, lucideX })],
  template: `
    @let image = current();
    <div class="flex h-full flex-col bg-slate-950/95">
      <div class="flex items-center gap-2 px-4 py-2 text-slate-100">
        <span class="min-w-0 flex-1 truncate text-sm">{{ image.fileName }}</span>
        @if (data.images.length > 1) {
          <span class="text-sm text-slate-400 tabular-nums"
            >{{ index() + 1 }} / {{ data.images.length }}</span
          >
        }
        <a
          matIconButton
          [href]="image.url"
          [attr.download]="image.fileName"
          aria-label="Download"
          class="!text-slate-100"
        >
          <ng-icon name="lucideDownload" size="18" />
        </a>
        <button
          matIconButton
          mat-dialog-close
          type="button"
          aria-label="Close"
          class="!text-slate-100"
        >
          <ng-icon name="lucideX" size="18" />
        </button>
      </div>
      <div class="relative flex min-h-0 flex-1 items-center justify-center px-14 pb-6">
        <img
          [src]="image.url"
          [alt]="image.fileName"
          class="max-h-full max-w-full rounded-control object-contain shadow-dialog"
        />
        @if (data.images.length > 1) {
          <button
            matIconButton
            type="button"
            class="!absolute top-1/2 left-2 -translate-y-1/2 !text-slate-100"
            aria-label="Previous image"
            (click)="move(-1)"
          >
            <ng-icon name="lucideChevronLeft" size="24" />
          </button>
          <button
            matIconButton
            type="button"
            class="!absolute top-1/2 right-2 -translate-y-1/2 !text-slate-100"
            aria-label="Next image"
            (click)="move(1)"
          >
            <ng-icon name="lucideChevronRight" size="24" />
          </button>
        }
      </div>
    </div>
  `,
  host: {
    '(document:keydown.arrowleft)': 'move(-1)',
    '(document:keydown.arrowright)': 'move(1)',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AttachmentLightbox {
  protected readonly data = inject<LightboxData>(MAT_DIALOG_DATA);
  protected readonly index = signal(this.data.index);
  protected readonly current = computed(() => this.data.images[this.index()]);

  protected move(step: number): void {
    const count = this.data.images.length;
    this.index.update((i) => (i + step + count) % count);
  }
}
