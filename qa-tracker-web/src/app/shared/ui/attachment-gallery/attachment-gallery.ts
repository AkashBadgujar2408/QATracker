import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileSpreadsheet, lucideFileText, lucideImage, lucideTrash2 } from '@ng-icons/lucide';
import { Attachment } from '../../data/attachments.api';
import { FileSizePipe } from '../../pipes/file-size.pipe';
import { IstDatePipe } from '../../pipes/ist-date.pipe';
import { AttachmentLightbox, LightboxData } from './attachment-lightbox';

/**
 * Issue attachments: image thumbnails open a lightbox, other files download.
 * <app-attachment-gallery [attachments]="list" [canRemove]="canEdit" (remove)="delete($event)" />
 */
@Component({
  selector: 'app-attachment-gallery',
  imports: [MatIconButton, NgIcon, FileSizePipe, IstDatePipe],
  viewProviders: [
    provideIcons({ lucideFileSpreadsheet, lucideFileText, lucideImage, lucideTrash2 }),
  ],
  template: `
    <ul class="m-0 grid list-none grid-cols-[repeat(auto-fill,minmax(9.5rem,1fr))] gap-3 p-0">
      @for (attachment of attachments(); track attachment.id) {
        <li class="group relative overflow-hidden rounded-card border border-border bg-surface">
          @if (attachment.isImage) {
            <button
              type="button"
              class="block w-full cursor-zoom-in border-0 bg-surface-muted p-0"
              [attr.aria-label]="'View ' + attachment.fileName"
              (click)="open(attachment)"
            >
              @if (attachment.thumbnailUrl) {
                <img
                  [src]="attachment.thumbnailUrl"
                  [alt]="attachment.fileName"
                  loading="lazy"
                  class="block h-28 w-full object-cover"
                />
              } @else {
                <span class="flex h-28 items-center justify-center text-muted">
                  <ng-icon name="lucideImage" size="28" />
                </span>
              }
            </button>
          } @else {
            <a
              [href]="attachment.url"
              [attr.download]="attachment.fileName"
              class="flex h-28 items-center justify-center bg-surface-muted text-muted no-underline hover:text-primary"
              [attr.aria-label]="'Download ' + attachment.fileName"
            >
              <ng-icon [name]="iconFor(attachment)" size="32" />
            </a>
          }
          <div class="px-2.5 py-2">
            <a
              [href]="attachment.url"
              target="_blank"
              rel="noopener"
              class="block truncate text-sm font-medium text-text no-underline hover:text-primary"
              [title]="attachment.fileName"
              >{{ attachment.fileName }}</a
            >
            <div class="truncate text-xs text-muted">
              {{ attachment.sizeBytes | fileSize }}, {{ attachment.uploadedAt | istDate }}
            </div>
          </div>
          @if (canRemove()) {
            <button
              matIconButton
              type="button"
              class="qa-gallery-remove !absolute top-1.5 right-1.5 opacity-0 transition-opacity group-focus-within:opacity-100 group-hover:opacity-100 motion-reduce:transition-none"
              [attr.aria-label]="'Remove ' + attachment.fileName"
              (click)="remove.emit(attachment)"
            >
              <ng-icon name="lucideTrash2" size="15" />
            </button>
          }
        </li>
      }
    </ul>
  `,
  styles: `
    .qa-gallery-remove {
      --mat-icon-button-state-layer-size: 30px;
      width: 30px;
      height: 30px;
      padding: 0;
      background: color-mix(in srgb, var(--qa-surface) 90%, transparent);
    }
    @media (hover: none) {
      .qa-gallery-remove {
        opacity: 1;
      }
    }
  `,
  host: { class: 'block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AttachmentGallery {
  private readonly dialog = inject(MatDialog);

  readonly attachments = input.required<readonly Attachment[]>();
  readonly canRemove = input(false, { transform: booleanAttribute });
  readonly remove = output<Attachment>();

  private readonly images = computed(() => this.attachments().filter((a) => a.isImage));

  protected iconFor(attachment: Attachment): string {
    return attachment.contentType.includes('spreadsheet')
      ? 'lucideFileSpreadsheet'
      : 'lucideFileText';
  }

  protected open(attachment: Attachment): void {
    const images = this.images();
    this.dialog.open<AttachmentLightbox, LightboxData>(AttachmentLightbox, {
      data: { images, index: images.indexOf(attachment) },
      width: '100vw',
      height: '100vh',
      maxWidth: '100vw',
      panelClass: 'qa-lightbox',
      ariaLabel: 'Image viewer',
    });
  }
}
