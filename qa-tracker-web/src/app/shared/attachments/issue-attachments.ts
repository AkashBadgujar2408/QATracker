import { HttpEventType } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MatProgressBar } from '@angular/material/progress-bar';
import { problemMessage, problemOf } from '../../core/api/api';
import { ConfirmService } from '../../core/feedback/confirm.service';
import { ToastService } from '../../core/feedback/toast.service';
import { Attachment, AttachmentsApi } from '../data/attachments.api';
import { AttachmentGallery } from '../ui/attachment-gallery/attachment-gallery';
import { PasteDropzone } from '../ui/paste-dropzone/paste-dropzone';
import { SkeletonLines } from '../ui/skeleton/skeleton';

interface Upload {
  readonly id: number;
  readonly names: string;
  readonly percent: number;
  readonly error: string | null;
}

let nextUploadId = 1;

/**
 * Attachments of an existing issue: gallery plus, when the user may edit, a dropzone that uploads at once
 * (Ctrl+V anywhere on the page with `capturePaste`).
 */
@Component({
  selector: 'app-issue-attachments',
  imports: [MatButton, MatProgressBar, AttachmentGallery, PasteDropzone, SkeletonLines],
  template: `
    @if (canEdit()) {
      <app-paste-dropzone
        [showPending]="false"
        [capturePaste]="capturePaste()"
        (added)="upload($event)"
        class="mb-3"
      />
    }

    @for (item of uploads(); track item.id) {
      <div class="mb-3 rounded-control border border-border px-3 py-2 text-sm" aria-live="polite">
        <div class="flex items-center gap-3">
          <span class="min-w-0 flex-1 truncate">{{ item.names }}</span>
          @if (item.error) {
            <button matButton type="button" (click)="dismiss(item.id)">Dismiss</button>
          } @else {
            <span class="text-muted tabular-nums">{{ item.percent }}%</span>
          }
        </div>
        @if (item.error) {
          <p role="alert" class="m-0 mt-1 text-severity-critical">{{ item.error }}</p>
        } @else {
          <mat-progress-bar mode="determinate" [value]="item.percent" class="mt-2" />
        }
      </div>
    }

    @if (attachments.isLoading() && !attachments.hasValue()) {
      <app-skeleton-lines [lines]="2" />
    } @else if (attachments.error()) {
      <p role="alert" class="m-0 text-sm text-severity-critical">
        Could not load attachments. {{ loadError() }}
        <button matButton type="button" (click)="attachments.reload()">Try again</button>
      </p>
    } @else if (list().length) {
      <app-attachment-gallery
        [attachments]="list()"
        [canRemove]="canEdit()"
        (remove)="remove($event)"
      />
    } @else if (!canEdit()) {
      <p class="m-0 text-sm text-muted">No attachments.</p>
    }
  `,
  host: { class: 'block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IssueAttachments {
  private readonly api = inject(AttachmentsApi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);

  readonly issueKey = input.required<string>();
  readonly canEdit = input(false, { transform: booleanAttribute });
  readonly capturePaste = input(false, { transform: booleanAttribute });

  protected readonly attachments = rxResource({
    params: () => this.issueKey(),
    stream: ({ params }) => this.api.list(params),
  });
  protected readonly list = computed(() =>
    this.attachments.hasValue() ? this.attachments.value() : [],
  );
  protected readonly loadError = computed(() => problemMessage(this.attachments.error()));
  protected readonly uploads = signal<Upload[]>([]);

  protected upload(files: File[]): void {
    const id = nextUploadId++;
    this.uploads.update((u) => [
      ...u,
      { id, names: files.map((f) => f.name).join(', '), percent: 0, error: null },
    ]);
    const patch = (change: Partial<Upload>) =>
      this.uploads.update((all) => all.map((u) => (u.id === id ? { ...u, ...change } : u)));

    this.api.upload(this.issueKey(), files).subscribe({
      next: (event) => {
        if (event.type === HttpEventType.UploadProgress && event.total) {
          patch({ percent: Math.round((100 * event.loaded) / event.total) });
        } else if (event.type === HttpEventType.Response) {
          this.dismiss(id);
          this.attachments.reload();
        }
      },
      error: (error: unknown) => {
        const problem = problemOf(error);
        patch({ error: problem?.errors?.['files']?.[0] ?? problemMessage(error) });
      },
    });
  }

  protected dismiss(id: number): void {
    this.uploads.update((all) => all.filter((u) => u.id !== id));
  }

  protected async remove(attachment: Attachment): Promise<void> {
    const ok = await this.confirm.ask({
      title: `Remove ${attachment.fileName}?`,
      message: 'The file is deleted. The issue history notes that it was removed.',
      confirmLabel: 'Remove',
      danger: true,
    });
    if (!ok) {
      return;
    }
    this.api.remove(attachment.id).subscribe({
      next: () => {
        this.toast.success(`${attachment.fileName} removed.`);
        this.attachments.reload();
      },
      error: (error: unknown) => this.toast.error(problemMessage(error)),
    });
  }
}
