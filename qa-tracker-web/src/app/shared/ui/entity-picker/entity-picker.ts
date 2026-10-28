import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  ElementRef,
  booleanAttribute,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ControlValueAccessor, FormControl, NgControl, ReactiveFormsModule } from '@angular/forms';
import {
  MatAutocomplete,
  MatAutocompleteSelectedEvent,
  MatAutocompleteTrigger,
  MatOption,
} from '@angular/material/autocomplete';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatError, MatFormField, MatHint, MatLabel, MatSuffix } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown, lucidePlus } from '@ng-icons/lucide';
import { fieldError } from '../../forms/form-errors';

export interface PickerItem {
  readonly id: number;
  readonly label: string;
  /** Secondary text shown on the right (a project key, an environment URL …). */
  readonly hint?: string;
}

/** Creates an item from the typed text (usually opens a quick-add dialog); null = cancelled. */
export type QuickAdd = (text: string) => Promise<PickerItem | null>;

const ADD_NEW = -1;

function normalize(text: string): string {
  return text.trim().toLowerCase();
}

/**
 * Searchable dropdown with "+ Add 'typed text'" (plan 8.1, 10.3). Arrow keys / Enter / Esc come from
 * mat-autocomplete. Works with `[(value)]` or as a form control (`formControlName`) – the value is an id.
 *
 * Usage: <app-entity-picker label="Module" noun="module" [items]="modules()" [quickAdd]="addModule"
 *          formControlName="moduleId" (created)="reload()" />
 */
@Component({
  selector: 'app-entity-picker',
  imports: [
    ReactiveFormsModule,
    MatAutocomplete,
    MatAutocompleteTrigger,
    MatOption,
    MatFormField,
    MatLabel,
    MatInput,
    MatError,
    MatHint,
    MatSuffix,
    NgIcon,
  ],
  viewProviders: [provideIcons({ lucideChevronDown, lucidePlus })],
  template: `
    <mat-form-field appearance="outline" class="w-full" [subscriptSizing]="subscriptSizing()">
      <mat-label>{{ label() }}</mat-label>
      <input
        #input
        matInput
        [formControl]="search"
        [matAutocomplete]="auto"
        [placeholder]="placeholder()"
        [required]="required()"
        [errorStateMatcher]="errorMatcher"
        autocomplete="off"
        spellcheck="false"
        (input)="onInput(input.value)"
        (blur)="onBlur()"
      />
      <ng-icon matSuffix name="lucideChevronDown" size="16" class="mr-3 text-muted" />
      @if (hint()) {
        <mat-hint>{{ hint() }}</mat-hint>
      }
      <mat-error>{{ errorText() }}</mat-error>
      <mat-autocomplete
        #auto="matAutocomplete"
        [displayWith]="display"
        [autoActiveFirstOption]="true"
        (optionSelected)="onSelected($event)"
        (closed)="reconcile()"
      >
        @if (loading()) {
          <mat-option disabled>Loading…</mat-option>
        } @else {
          @for (item of filtered(); track item.id) {
            <mat-option [value]="item.id">
              <span class="flex items-center gap-3">
                <span class="min-w-0 flex-1 truncate">{{ item.label }}</span>
                @if (item.hint) {
                  <span class="shrink-0 text-xs text-muted">{{ item.hint }}</span>
                }
              </span>
            </mat-option>
          }
          @if (canAdd()) {
            <mat-option [value]="addNew" class="qa-picker-add">
              <span class="flex items-center gap-2 text-primary">
                <ng-icon name="lucidePlus" size="16" />
                <span class="truncate"
                  >Add “<strong class="font-semibold">{{ query() }}</strong
                  >” as new {{ noun() }}</span
                >
              </span>
            </mat-option>
          } @else if (filtered().length === 0) {
            <mat-option disabled>{{ emptyText() }}</mat-option>
          }
        }
      </mat-autocomplete>
    </mat-form-field>
  `,
  host: { class: 'block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EntityPicker implements ControlValueAccessor {
  readonly label = input.required<string>();
  readonly items = input<readonly PickerItem[]>([]);
  /** Lower-case name of one item, used in "Add … as new module". */
  readonly noun = input('item');
  readonly placeholder = input('Type to search');
  readonly hint = input<string>();
  readonly quickAdd = input<QuickAdd | null>(null);
  readonly loading = input(false, { transform: booleanAttribute });
  readonly required = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly subscriptSizing = input<'fixed' | 'dynamic'>('fixed');

  /** The selected id. */
  readonly value = model<number | null>(null);
  /** Emitted after quick-add created (or chose) an item; reload the item list. */
  readonly created = output<PickerItem>();

  protected readonly addNew = ADD_NEW;
  protected readonly search = new FormControl<string | number | null>('');
  protected readonly typing = signal(false);
  /** What the user last typed (selecting the "Add" option overwrites the input value). */
  private typedText = '';
  private readonly text = toSignal(this.search.valueChanges, { initialValue: '' });
  /** Quick-added items until the parent's list includes them. */
  private readonly extra = signal<readonly PickerItem[]>([]);
  private readonly disabledByForm = signal(false);

  private readonly trigger = viewChild.required(MatAutocompleteTrigger);
  private readonly inputRef = viewChild.required<ElementRef<HTMLInputElement>>('input');
  private readonly ngControl = inject(NgControl, { self: true, optional: true });
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);

  private readonly allItems = computed(() => {
    const items = this.items();
    const ids = new Set(items.map((item) => item.id));
    return [...items, ...this.extra().filter((item) => !ids.has(item.id))];
  });

  protected readonly query = computed(() => {
    const text = this.text();
    return this.typing() && typeof text === 'string' ? text.trim() : '';
  });

  protected readonly filtered = computed(() => {
    const query = normalize(this.query());
    if (!query) {
      return this.allItems();
    }
    return this.allItems().filter(
      (item) =>
        normalize(item.label).includes(query) ||
        (item.hint && normalize(item.hint).includes(query)),
    );
  });

  protected readonly canAdd = computed(() => {
    const query = normalize(this.query());
    return (
      this.quickAdd() !== null &&
      query.length > 0 &&
      !this.allItems().some((item) => normalize(item.label) === query)
    );
  });

  protected readonly emptyText = computed(() =>
    this.query() ? 'No matches' : `No ${this.noun()}s yet – type a name to add one`,
  );

  protected readonly errorMatcher: ErrorStateMatcher = {
    isErrorState: () => {
      const control = this.ngControl?.control;
      if (control) {
        return control.invalid && (control.touched || control.dirty);
      }
      return this.required() && this.value() === null && this.search.touched;
    },
  };

  protected readonly display = (value: string | number | null): string =>
    typeof value === 'number'
      ? (this.allItems().find((i) => i.id === value)?.label ?? '')
      : (value ?? '');

  private onChange: (value: number | null) => void = () => undefined;
  private onTouched: () => void = () => undefined;

  constructor() {
    if (this.ngControl) {
      this.ngControl.valueAccessor = this;
    }

    // Show the selected item's label (also once the item list arrives) unless the user is typing.
    effect(() => {
      const value = this.value();
      this.allItems();
      untracked(() => {
        if (!this.typing()) {
          this.search.setValue(value ?? '', { emitEvent: false });
        }
      });
    });

    effect(() => {
      const disabled = this.disabled() || this.disabledByForm();
      untracked(() => (disabled ? this.search.disable() : this.search.enable()));
    });
  }

  protected errorText(): string {
    const control = this.ngControl?.control;
    return control ? fieldError(control, this.label()) : `${this.label()} is required.`;
  }

  protected onInput(text: string): void {
    this.typedText = text;
    this.typing.set(true);
  }

  protected onSelected(event: MatAutocompleteSelectedEvent): void {
    const id = event.option.value as number;
    if (id === ADD_NEW) {
      void this.add(this.typedText.trim());
    } else {
      this.choose(id);
    }
  }

  protected onBlur(): void {
    this.onTouched();
    if (!this.trigger().panelOpen) {
      this.reconcile();
    }
  }

  /** After typing without picking: empty clears, an exact name selects it, anything else reverts. */
  protected reconcile(): void {
    if (!this.typing()) {
      return;
    }
    const text = typeof this.search.value === 'string' ? normalize(this.search.value) : '';
    if (!text) {
      this.choose(null);
      return;
    }
    const match = this.allItems().find((item) => normalize(item.label) === text);
    if (match) {
      this.choose(match.id);
    } else {
      this.typing.set(false);
      this.search.setValue(this.value() ?? '', { emitEvent: false });
    }
  }

  private async add(text: string): Promise<void> {
    const quickAdd = this.quickAdd();
    this.search.setValue(text, { emitEvent: false });
    const item = quickAdd ? await quickAdd(text) : null;
    if (item) {
      this.extra.update((extra) => [...extra.filter((e) => e.id !== item.id), item]);
      this.choose(item.id);
      this.created.emit(item);
    } else {
      this.typing.set(false);
      this.search.setValue(this.value() ?? '', { emitEvent: false });
    }
    this.cdr.markForCheck();
    this.inputRef().nativeElement.focus();
  }

  private choose(id: number | null): void {
    this.typing.set(false);
    this.search.setValue(id ?? '', { emitEvent: false });
    if (id !== this.value()) {
      this.value.set(id);
      this.onChange(id);
    }
  }

  // ControlValueAccessor
  writeValue(value: number | null): void {
    this.typing.set(false);
    this.value.set(value ?? null);
  }

  registerOnChange(fn: (value: number | null) => void): void {
    this.onChange = fn;
    // Re-check the error state when the parent form is touched or validated (e.g. on submit).
    this.ngControl?.control?.events
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.cdr.markForCheck());
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.disabledByForm.set(disabled);
  }
}
