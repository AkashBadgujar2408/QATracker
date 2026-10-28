import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  booleanAttribute,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideFileText, lucideImagePlus, lucideX } from '@ng-icons/lucide';
import { ATTACHMENT_RULES, attachmentProblem } from '../../data/attachments.api';
import { FileSizePipe } from '../../pipes/file-size.pipe';

interface Preview {
  readonly file: File;
  readonly url: string | null;
}

/** "screenshot-2026-10-28-101530.png" for an unnamed pasted image. */
export function screenshotName(file: File, now = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  const stamp = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
  const extension = file.type.split('/')[1]?.replace('jpeg', 'jpg') ?? 'png';
  return `screenshot-${stamp}.${extension}`;
}

/**
 * Collects files by Ctrl+V (screenshots), drag and drop or the file picker (plan 8.1). Checks type and size like the
 * API, shows previews of `files` with a remove button, and emits `added` with each batch of accepted files.
 *
 * Keep files until save: <app-paste-dropzone [(files)]="pending" capturePaste />
 * Upload at once:      <app-paste-dropzone [showPending]="false" (added)="upload($event)" />
 */
@Component({
  selector: 'app-paste-dropzone',
  imports: [MatIconButton, NgIcon, FileSizePipe],
  viewProviders: [provideIcons({ lucideFileText, lucideImagePlus, lucideX })],
  template: `
    <div
      role="button"
      tabindex="0"
      class="flex cursor-pointer flex-col items-center justify-center gap-1 rounded-card border-2 border-dashed px-4 py-5 text-center transition-colors motion-reduce:transition-none"
      [class]="
        dragging()
          ? 'border-primary bg-primary/5 text-primary'
          : 'border-border-strong text-muted hover:border-primary hover:text-text'
      "
      [class.pointer-events-none]="disabled()"
      [class.opacity-50]="disabled()"
      [attr.aria-disabled]="disabled()"
      [attr.aria-label]="'Add attachments. ' + hint()"
      (click)="picker.click()"
      (keydown.enter)="picker.click()"
      (keydown.space)="$event.preventDefault(); picker.click()"
      (dragenter)="onDrag($event, true)"
      (dragover)="onDrag($event, true)"
      (dragleave)="onDrag($event, false)"
      (drop)="onDrop($event)"
    >
      <ng-icon name="lucideImagePlus" size="22" aria-hidden="true" />
      <span class="text-sm font-medium text-text">{{ hint() }}</span>
      <span class="text-xs">Images, PDF, Excel, Word or text – up to 10 MB each</span>
    </div>
    <input
      #picker
      type="file"
      multiple
      class="hidden"
      [accept]="accept"
      (change)="onPicked(picker)"
    />

    @if (errors().length) {
      <ul role="alert" class="m-0 mt-2 list-none p-0 text-sm text-severity-critical">
        @for (error of errors(); track $index) {
          <li>{{ error }}</li>
        }
      </ul>
    }

    @if (showPending() && previews().length) {
      <ul
        class="m-0 mt-3 grid list-none grid-cols-[repeat(auto-fill,minmax(7.5rem,1fr))] gap-2 p-0"
      >
        @for (preview of previews(); track preview.file) {
          <li class="relative overflow-hidden rounded-control border border-border bg-surface">
            @if (preview.url) {
              <img
                [src]="preview.url"
                [alt]="preview.file.name"
                class="block h-20 w-full object-cover"
              />
            } @else {
              <div class="flex h-20 items-center justify-center bg-surface-muted text-muted">
                <ng-icon name="lucideFileText" size="24" />
              </div>
            }
            <div class="px-2 py-1">
              <div class="truncate text-xs font-medium text-text" [title]="preview.file.name">
                {{ preview.file.name }}
              </div>
              <div class="text-xs text-muted tabular-nums">{{ preview.file.size | fileSize }}</div>
            </div>
            <button
              matIconButton
              type="button"
              class="qa-dropzone-remove !absolute top-1 right-1"
              [attr.aria-label]="'Remove ' + preview.file.name"
              (click)="remove(preview.file)"
            >
              <ng-icon name="lucideX" size="14" />
            </button>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .qa-dropzone-remove {
      --mat-icon-button-state-layer-size: 28px;
      width: 28px;
      height: 28px;
      padding: 0;
      background: color-mix(in srgb, var(--qa-surface) 85%, transparent);
    }
  `,
  host: {
    class: 'block',
    '(paste)': 'onPaste($event, false)',
    '(document:paste)': 'onPaste($event, true)',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PasteDropzone {
  /** Files collected so far (shown as previews when `showPending`). */
  readonly files = model<readonly File[]>([]);
  /** Accept Ctrl+V anywhere on the page (the Testing form), not only when focus is inside. */
  readonly capturePaste = input(false, { transform: booleanAttribute });
  readonly showPending = input(true, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly hint = input('Paste a screenshot (Ctrl+V), drop files here or browse');
  /** Each batch of accepted files. */
  readonly added = output<File[]>();

  protected readonly accept = ATTACHMENT_RULES.extensions.join(',');
  protected readonly dragging = signal(false);
  protected readonly errors = signal<string[]>([]);
  protected readonly previews = signal<Preview[]>([]);

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly picker = viewChild.required<ElementRef<HTMLInputElement>>('picker');

  constructor() {
    // Object URLs for image previews, released when the list changes or the component goes away.
    effect((onCleanup) => {
      const canPreview = typeof URL.createObjectURL === 'function';
      const previews = this.files().map((file) => ({
        file,
        url: canPreview && file.type.startsWith('image/') ? URL.createObjectURL(file) : null,
      }));
      this.previews.set(previews);
      onCleanup(() => previews.forEach((p) => p.url && URL.revokeObjectURL(p.url)));
    });
  }

  /** Adds files as if picked (also used by parents, e.g. for files dropped elsewhere). */
  addFiles(files: readonly File[]): void {
    if (this.disabled() || files.length === 0) {
      return;
    }
    const problems: string[] = [];
    const accepted: File[] = [];
    for (const file of files) {
      const problem = attachmentProblem(file);
      if (problem) {
        problems.push(problem);
      } else {
        accepted.push(file);
      }
    }
    this.errors.set(problems);
    if (accepted.length) {
      this.files.update((current) => [...current, ...accepted]);
      this.added.emit(accepted);
    }
  }

  remove(file: File): void {
    this.files.update((current) => current.filter((f) => f !== file));
  }

  protected onPaste(event: ClipboardEvent, fromDocument: boolean): void {
    const inside = this.host.nativeElement.contains(event.target as Node | null);
    // The host listener handles pastes inside; the document listener the rest (only with capturePaste).
    if (
      fromDocument === inside ||
      (fromDocument && !this.capturePaste()) ||
      event.defaultPrevented
    ) {
      return;
    }
    const files = Array.from(event.clipboardData?.files ?? []);
    if (files.length === 0) {
      return; // plain text paste – leave it to the focused field
    }
    event.preventDefault();
    this.addFiles(
      files.map((file) =>
        file.type.startsWith('image/') && /^image\.\w+$/i.test(file.name)
          ? new File([file], screenshotName(file), { type: file.type })
          : file,
      ),
    );
  }

  protected onDrag(event: DragEvent, over: boolean): void {
    event.preventDefault();
    if (!this.disabled()) {
      this.dragging.set(over);
    }
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.addFiles(Array.from(event.dataTransfer?.files ?? []));
  }

  protected onPicked(input: HTMLInputElement): void {
    this.addFiles(Array.from(input.files ?? []));
    input.value = '';
  }

  /** Opens the file picker (for a separate "Attach" button). */
  browse(): void {
    this.picker().nativeElement.click();
  }
}
