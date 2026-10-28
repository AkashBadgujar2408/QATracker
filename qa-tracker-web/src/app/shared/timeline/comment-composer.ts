import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePaperclip, lucideX } from '@ng-icons/lucide';
import { problemMessage } from '../../core/api/api';
import { ATTACHMENT_RULES, attachmentProblem } from '../data/attachments.api';
import { UserLookup } from '../data/masters.api';
import { Activity, TimelineApi } from '../data/timeline.api';
import { FileSizePipe } from '../pipes/file-size.pipe';
import { screenshotName } from '../ui/paste-dropzone/paste-dropzone';

/** The "@partial" right before the caret, or null. */
export function mentionQuery(text: string, caret: number): { start: number; query: string } | null {
  const match = /(^|[\s(])@([A-Za-z0-9._-]{0,50})$/.exec(text.slice(0, caret));
  return match ? { start: caret - match[2].length - 1, query: match[2] } : null;
}

/**
 * Comment box (plan 7.5): plain text, @ opens user suggestions (↑ ↓ Enter/Tab, Esc), Ctrl+V / drop / clip adds files,
 * Ctrl+Enter sends. Emits the created comment.
 */
@Component({
  selector: 'app-comment-composer',
  imports: [MatButton, MatIconButton, NgIcon, FileSizePipe],
  viewProviders: [provideIcons({ lucidePaperclip, lucideX })],
  template: `
    <div
      class="relative rounded-card border bg-surface"
      [class]="dragging() ? 'border-primary' : 'border-border-strong'"
      (dragover)="$event.preventDefault(); dragging.set(true)"
      (dragleave)="dragging.set(false)"
      (drop)="onDrop($event)"
    >
      <label for="qa-comment-box" class="sr-only">Comment</label>
      <textarea
        #box
        id="qa-comment-box"
        rows="3"
        class="block w-full resize-y rounded-t-card border-0 bg-transparent px-3 py-2.5 text-base text-text outline-none"
        placeholder="Write a comment – @ to mention, Ctrl+V to paste a screenshot"
        [value]="text()"
        [attr.aria-expanded]="suggestions().length > 0"
        aria-controls="qa-mention-list"
        aria-autocomplete="list"
        (input)="onInput()"
        (click)="onInput()"
        (keydown)="onKeydown($event)"
        (paste)="onPaste($event)"
        (blur)="closeSoon()"
      ></textarea>

      @if (suggestions().length) {
        <ul
          id="qa-mention-list"
          role="listbox"
          class="absolute left-3 z-20 m-0 mt-[-0.25rem] max-h-56 w-72 list-none overflow-y-auto rounded-control border border-border bg-surface p-1 shadow-dialog"
        >
          @for (user of suggestions(); track user.id; let i = $index) {
            <li
              role="option"
              class="flex cursor-pointer items-center gap-2 rounded-control px-2 py-1.5 text-sm"
              [class.bg-primary/10]="i === active()"
              [attr.aria-selected]="i === active()"
              (mousedown)="$event.preventDefault(); insert(user)"
            >
              <span class="min-w-0 flex-1 truncate text-text">{{ user.fullName }}</span>
              <span class="text-xs text-muted">{{ user.userName }}</span>
            </li>
          }
        </ul>
      }

      @if (files().length) {
        <ul class="m-0 flex list-none flex-wrap gap-2 px-3 pb-2">
          @for (file of files(); track file) {
            <li
              class="flex items-center gap-1 rounded-pill border border-border bg-surface-muted py-0.5 pr-1 pl-3 text-xs"
            >
              <span class="max-w-48 truncate">{{ file.name }}</span>
              <span class="text-muted">{{ file.size | fileSize }}</span>
              <button
                matIconButton
                type="button"
                class="qa-chip-remove"
                [attr.aria-label]="'Remove ' + file.name"
                (click)="removeFile(file)"
              >
                <ng-icon name="lucideX" size="12" />
              </button>
            </li>
          }
        </ul>
      }

      <div class="flex items-center gap-2 border-t border-border px-2 py-1.5">
        <button matIconButton type="button" aria-label="Attach files" (click)="picker.click()">
          <ng-icon name="lucidePaperclip" size="16" />
        </button>
        <input
          #picker
          type="file"
          multiple
          class="hidden"
          [accept]="accept"
          (change)="addFiles(picker.files); picker.value = ''"
        />
        @if (error()) {
          <span role="alert" class="min-w-0 flex-1 truncate text-sm text-severity-critical">{{
            error()
          }}</span>
        } @else {
          <span class="flex-1 text-xs text-muted">Ctrl+Enter to send</span>
        }
        <button
          matButton="filled"
          type="button"
          [disabled]="sending() || (!text().trim() && !files().length)"
          (click)="send()"
        >
          {{ sending() ? 'Sending…' : 'Comment' }}
        </button>
      </div>
    </div>
  `,
  styles: `
    .qa-chip-remove {
      --mat-icon-button-state-layer-size: 22px;
      width: 22px;
      height: 22px;
      padding: 0;
    }
  `,
  host: { class: 'block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentComposer {
  readonly issueKey = input.required<string>();
  /** People who can be mentioned. */
  readonly users = input<readonly UserLookup[]>([]);
  readonly posted = output<Activity>();

  private readonly api = inject(TimelineApi);
  private readonly box = viewChild.required<ElementRef<HTMLTextAreaElement>>('box');

  protected readonly accept = ATTACHMENT_RULES.extensions.join(',');
  protected readonly text = signal('');
  protected readonly files = signal<readonly File[]>([]);
  protected readonly sending = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly dragging = signal(false);
  protected readonly active = signal(0);
  private readonly mention = signal<{ start: number; query: string } | null>(null);

  protected readonly suggestions = computed(() => {
    const mention = this.mention();
    if (!mention) {
      return [];
    }
    const query = mention.query.toLowerCase();
    return this.users()
      .filter(
        (u) => u.userName.toLowerCase().includes(query) || u.fullName.toLowerCase().includes(query),
      )
      .slice(0, 8);
  });

  focus(): void {
    this.box().nativeElement.focus();
  }

  protected onInput(): void {
    const box = this.box().nativeElement;
    this.text.set(box.value);
    this.mention.set(mentionQuery(box.value, box.selectionStart ?? box.value.length));
    this.active.set(0);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
      event.preventDefault();
      this.send();
      return;
    }
    const list = this.suggestions();
    if (!list.length) {
      return;
    }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      const step = event.key === 'ArrowDown' ? 1 : -1;
      this.active.set((this.active() + step + list.length) % list.length);
    } else if (event.key === 'Enter' || event.key === 'Tab') {
      event.preventDefault();
      this.insert(list[this.active()]);
    } else if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      this.mention.set(null);
    }
  }

  protected insert(user: UserLookup): void {
    const mention = this.mention();
    const box = this.box().nativeElement;
    if (!mention) {
      return;
    }
    const caret = box.selectionStart ?? box.value.length;
    const value = `${box.value.slice(0, mention.start)}@${user.userName} ${box.value.slice(caret)}`;
    const position = mention.start + user.userName.length + 2;
    box.value = value;
    box.setSelectionRange(position, position);
    this.text.set(value);
    this.mention.set(null);
    box.focus();
  }

  protected closeSoon(): void {
    setTimeout(() => this.mention.set(null), 150);
  }

  protected onPaste(event: ClipboardEvent): void {
    const pasted = Array.from(event.clipboardData?.files ?? []);
    if (!pasted.length) {
      return;
    }
    event.preventDefault();
    this.addFiles(
      pasted.map((file) =>
        file.type.startsWith('image/') && /^image\.\w+$/i.test(file.name)
          ? new File([file], screenshotName(file), { type: file.type })
          : file,
      ),
    );
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.addFiles(event.dataTransfer?.files ?? null);
  }

  protected addFiles(list: FileList | readonly File[] | null): void {
    const incoming = Array.from(list ?? []);
    const problems = incoming.map(attachmentProblem).filter((p): p is string => p !== null);
    const accepted = incoming.filter((f) => attachmentProblem(f) === null);
    const room = Math.max(ATTACHMENT_RULES.maxFilesPerUpload - this.files().length, 0);
    const kept = accepted.slice(0, room);
    this.files.update((files) => [...files, ...kept]);
    this.error.set(
      problems[0] ??
        (kept.length < accepted.length
          ? `At most ${ATTACHMENT_RULES.maxFilesPerUpload} files per comment.`
          : null),
    );
  }

  protected removeFile(file: File): void {
    this.files.update((files) => files.filter((f) => f !== file));
  }

  /** Posts the comment; also used by action dialogs (reopen with screenshot). */
  send(): void {
    const body = this.text().trim();
    if (this.sending() || (!body && !this.files().length)) {
      return;
    }
    this.sending.set(true);
    this.error.set(null);
    this.api.comment(this.issueKey(), body, this.files()).subscribe({
      next: (comment) => {
        this.sending.set(false);
        this.text.set('');
        this.box().nativeElement.value = '';
        this.files.set([]);
        this.posted.emit(comment);
      },
      error: (error: unknown) => {
        this.sending.set(false);
        this.error.set(problemMessage(error));
      },
    });
  }
}
