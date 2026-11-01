import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  SecurityContext,
  afterNextRender,
  booleanAttribute,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ControlValueAccessor, NgControl } from '@angular/forms';
import { DomSanitizer } from '@angular/platform-browser';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBold,
  lucideCode,
  lucideItalic,
  lucideList,
  lucideListOrdered,
  lucideUnderline,
} from '@ng-icons/lucide';
import { fieldError } from '../../forms/form-errors';

type Command = 'bold' | 'italic' | 'underline' | 'insertUnorderedList' | 'insertOrderedList';

interface ToolbarButton {
  readonly command: Command | 'code';
  readonly icon: string;
  readonly label: string;
}

const TOOLBAR: readonly ToolbarButton[] = [
  { command: 'bold', icon: 'lucideBold', label: 'Bold (Ctrl+B)' },
  { command: 'italic', icon: 'lucideItalic', label: 'Italic (Ctrl+I)' },
  { command: 'underline', icon: 'lucideUnderline', label: 'Underline (Ctrl+U)' },
  { command: 'insertUnorderedList', icon: 'lucideList', label: 'Bulleted list' },
  { command: 'insertOrderedList', icon: 'lucideListOrdered', label: 'Numbered list' },
  { command: 'code', icon: 'lucideCode', label: 'Code' },
];

let nextId = 1;

/** True when the HTML has no visible text (an empty editor holds "<br>" or "<p></p>"). */
export function isBlankHtml(html: string | null | undefined): boolean {
  return (
    !html ||
    html
      .replace(/<[^>]*>/g, '')
      .replace(/&nbsp;/g, ' ')
      .trim().length === 0
  );
}

/**
 * Small rich-text field (plan 10.3 `rich-text-editor`): bold, italic, underline, lists, inline code. The value is
 * HTML (null when empty); the API sanitises it again (ADR-018). Pasted text arrives as plain text, pasted images are
 * left to a page-wide paste-dropzone. Use as a form control: <app-rich-text-editor label="Steps" formControlName="steps" />
 */
@Component({
  selector: 'app-rich-text-editor',
  imports: [NgIcon],
  viewProviders: [
    provideIcons({
      lucideBold,
      lucideCode,
      lucideItalic,
      lucideList,
      lucideListOrdered,
      lucideUnderline,
    }),
  ],
  template: `
    <div
      [id]="labelId"
      class="mb-1 block text-sm font-medium"
      [class]="invalid() ? 'text-danger' : 'text-muted'"
    >
      {{ label() }}{{ required() ? ' *' : '' }}
    </div>
    <div
      class="qa-rte overflow-hidden rounded-control border bg-surface"
      [class]="invalid() ? 'border-severity-critical' : 'border-border-strong'"
      [class.opacity-60]="isDisabled()"
    >
      <div
        class="flex items-center gap-0.5 border-b border-border px-1 py-0.5"
        role="toolbar"
        [attr.aria-label]="label() + ' formatting'"
      >
        @for (button of toolbar; track button.command) {
          <button
            type="button"
            tabindex="-1"
            class="flex size-7 cursor-pointer items-center justify-center rounded-control border-0 bg-transparent text-muted hover:bg-surface-muted hover:text-text disabled:cursor-default"
            [title]="button.label"
            [attr.aria-label]="button.label"
            [disabled]="isDisabled()"
            (mousedown)="$event.preventDefault()"
            (click)="run(button.command)"
          >
            <ng-icon [name]="button.icon" size="15" />
          </button>
        }
      </div>
      <div
        #editor
        [id]="editorId"
        role="textbox"
        aria-multiline="true"
        tabindex="0"
        class="qa-rte-content px-3 py-2 text-base text-text outline-none"
        [style.min-height.rem]="rows() * 1.5 + 1"
        [attr.contenteditable]="!isDisabled()"
        [attr.aria-labelledby]="labelId"
        [attr.aria-required]="required()"
        [attr.aria-invalid]="invalid()"
        [attr.data-placeholder]="placeholder()"
        (input)="onInput()"
        (blur)="onTouched()"
        (paste)="onPaste($event)"
      ></div>
    </div>
    @if (invalid()) {
      <p class="m-0 mt-1 px-1 text-xs text-danger" role="alert">{{ errorText() }}</p>
    } @else if (hint()) {
      <p class="m-0 mt-1 px-1 text-xs text-muted">{{ hint() }}</p>
    }
  `,
  styles: `
    .qa-rte:focus-within {
      outline: 2px solid var(--qa-primary);
      outline-offset: -1px;
    }
    .qa-rte-content {
      max-height: 32rem;
      resize: vertical;
      overflow-y: auto;
      line-height: 1.5;
      overflow-wrap: anywhere;
    }
    .qa-rte-content:empty::before {
      content: attr(data-placeholder);
      color: var(--qa-text-muted);
      pointer-events: none;
    }
    .qa-rte-content :is(p, ul, ol) {
      margin: 0 0 0.25rem;
    }
    .qa-rte-content :is(ul, ol) {
      padding-left: 1.25rem;
    }
    .qa-rte-content code {
      font-size: 0.9em;
      padding: 0 0.25rem;
      border-radius: 4px;
      background: var(--qa-surface-muted);
    }
  `,
  host: { class: 'block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RichTextEditor implements ControlValueAccessor {
  readonly label = input.required<string>();
  readonly placeholder = input('');
  readonly hint = input<string>();
  readonly rows = input(3);
  readonly required = input(false, { transform: booleanAttribute });

  protected readonly toolbar = TOOLBAR;
  protected readonly editorId = `qa-rte-${nextId}`;
  protected readonly labelId = `qa-rte-label-${nextId++}`;

  private readonly editor = viewChild.required<ElementRef<HTMLDivElement>>('editor');
  private readonly ngControl = inject(NgControl, { self: true, optional: true });
  private readonly sanitizer = inject(DomSanitizer);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  private readonly disabledByForm = signal(false);
  protected readonly isDisabled = computed(() => this.disabledByForm());
  /** Bumped when the control's state changes so `invalid()` re-evaluates. */
  private readonly controlState = signal(0);

  protected readonly invalid = computed(() => {
    this.controlState();
    const control = this.ngControl?.control;
    return !!control && control.invalid && (control.touched || control.dirty);
  });

  private onChange: (value: string | null) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  focus(): void {
    this.editor().nativeElement.focus();
  }

  protected errorText(): string {
    const control = this.ngControl?.control;
    return control ? fieldError(control, this.label()) : '';
  }

  protected run(command: Command | 'code'): void {
    this.focus();
    if (command === 'code') {
      const text = document.getSelection()?.toString() ?? '';
      document.execCommand('insertHTML', false, `<code>${escapeHtml(text || 'code')}</code>&nbsp;`);
    } else {
      document.execCommand(command);
    }
    this.onInput();
  }

  protected onInput(): void {
    const html = this.editor().nativeElement.innerHTML;
    this.onChange(isBlankHtml(html) ? null : html);
  }

  protected onPaste(event: ClipboardEvent): void {
    if (event.clipboardData?.files.length) {
      return; // screenshots: handled by the page's paste-dropzone
    }
    const text = event.clipboardData?.getData('text/plain');
    if (text !== undefined) {
      event.preventDefault();
      document.execCommand('insertText', false, text);
    }
  }

  // ControlValueAccessor
  constructor() {
    if (this.ngControl) {
      this.ngControl.valueAccessor = this;
    }
  }

  writeValue(value: string | null): void {
    const safe = value ? (this.sanitizer.sanitize(SecurityContext.HTML, value) ?? '') : '';
    const editor = this.host.nativeElement.querySelector<HTMLElement>('.qa-rte-content');
    if (editor) {
      editor.innerHTML = safe;
    } else {
      // Form controls may write before the view exists.
      afterNextRender(() => (this.editor().nativeElement.innerHTML = safe), {
        injector: this.injector,
      });
    }
  }

  registerOnChange(fn: (value: string | null) => void): void {
    this.onChange = fn;
    this.ngControl?.control?.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.controlState.update((n) => n + 1);
      this.cdr.markForCheck();
    });
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.disabledByForm.set(disabled);
  }
}

function escapeHtml(text: string): string {
  return text.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);
}
