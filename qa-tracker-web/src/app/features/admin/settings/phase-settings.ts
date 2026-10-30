import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatSlideToggle } from '@angular/material/slide-toggle';
import { MatTooltip } from '@angular/material/tooltip';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowDown, lucideArrowUp } from '@ng-icons/lucide';
import { Observable } from 'rxjs';
import { problemMessage } from '../../../core/api/api';
import { ToastService } from '../../../core/feedback/toast.service';
import { PluralPipe } from '../../../shared/pipes/plural.pipe';
import { EmptyState } from '../../../shared/ui/empty-state/empty-state';
import { SkeletonLines } from '../../../shared/ui/skeleton/skeleton';
import { Phase, SettingsApi } from './settings.api';

/**
 * Sprint phases: the order and names "Start a testing sprint" offers (and that leakage charts use). Inactive phases
 * are hidden from new sprints; sprints already in them keep them.
 */
@Component({
  selector: 'app-phase-settings',
  imports: [
    FormsModule,
    MatButton,
    MatIconButton,
    MatFormField,
    MatLabel,
    MatInput,
    MatSlideToggle,
    MatTooltip,
    NgIcon,
    PluralPipe,
    EmptyState,
    SkeletonLines,
  ],
  viewProviders: [provideIcons({ lucideArrowUp, lucideArrowDown })],
  template: `
    <p class="m-0 mb-5 max-w-3xl text-muted">
      Phases name the stage a sprint tests (UAT, Live…). New sprints offer the active ones in this
      order. Switching a phase off hides it from new sprints; sprints already in it keep it.
    </p>

    <form class="mb-6 flex flex-wrap items-start gap-3" (ngSubmit)="add()">
      <mat-form-field class="min-w-56 flex-1 sm:max-w-sm" subscriptSizing="dynamic">
        <mat-label>New phase</mat-label>
        <input
          matInput
          name="name"
          maxlength="50"
          placeholder="Pilot"
          [ngModel]="newName()"
          (ngModelChange)="newName.set($event)"
        />
      </mat-form-field>
      <button
        matButton="filled"
        type="submit"
        class="mt-2"
        [disabled]="!newName().trim() || busy()"
      >
        Add phase
      </button>
    </form>

    @if (phases.isLoading() && !phases.hasValue()) {
      <app-skeleton-lines [lines]="5" />
    } @else if (phases.error() && !phases.hasValue()) {
      <app-empty-state kind="error" title="Phases could not be loaded" [message]="loadError()">
        <button matButton="outlined" type="button" (click)="phases.reload()">Try again</button>
      </app-empty-state>
    } @else if (list().length === 0) {
      <app-empty-state title="No phases yet" message="Add the first phase above." />
    } @else {
      <ol class="qa-phases m-0 list-none p-0">
        @for (p of list(); track p.id; let first = $first; let last = $last; let i = $index) {
          <li
            class="flex flex-wrap items-center gap-x-4 gap-y-2 py-2.5"
            [class.qa-inactive]="!p.isActive"
          >
            <span class="w-6 text-right text-sm text-muted tabular-nums" aria-hidden="true">{{
              i + 1
            }}</span>
            <input
              class="qa-name"
              maxlength="50"
              [value]="p.name"
              [attr.aria-label]="'Name of phase ' + p.name"
              [disabled]="busy()"
              (change)="rename(p, $any($event.target))"
              (keydown.enter)="$any($event.target).blur()"
            />
            <span class="w-24 text-sm text-muted">{{ p.sprintCount | plural: 'sprint' }}</span>
            <mat-slide-toggle
              [checked]="p.isActive"
              [disabled]="busy()"
              (change)="setActive(p, $event.checked)"
              >{{ p.isActive ? 'Active' : 'Off' }}</mat-slide-toggle
            >
            <span class="ml-auto flex">
              <button
                matIconButton
                type="button"
                matTooltip="Move up"
                [attr.aria-label]="'Move ' + p.name + ' up'"
                [disabled]="first || busy()"
                (click)="move(i, -1)"
              >
                <ng-icon name="lucideArrowUp" size="18" />
              </button>
              <button
                matIconButton
                type="button"
                matTooltip="Move down"
                [attr.aria-label]="'Move ' + p.name + ' down'"
                [disabled]="last || busy()"
                (click)="move(i, 1)"
              >
                <ng-icon name="lucideArrowDown" size="18" />
              </button>
            </span>
          </li>
        }
      </ol>
    }
  `,
  styles: `
    .qa-phases > li + li {
      border-top: 1px solid var(--qa-border);
    }
    .qa-name {
      min-width: 12rem;
      flex: 1 1 12rem;
      max-width: 22rem;
      padding: 5px 8px;
      border: 1px solid transparent;
      border-radius: var(--qa-radius-control);
      background: transparent;
      color: var(--qa-text);
      font: inherit;
      font-weight: 500;
    }
    .qa-name:hover:not(:disabled) {
      border-color: var(--qa-border);
    }
    .qa-name:focus {
      border-color: var(--qa-primary);
      outline: none;
      background: var(--qa-surface);
    }
    .qa-inactive .qa-name {
      color: var(--qa-text-muted);
      font-weight: 400;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PhaseSettings {
  private readonly api = inject(SettingsApi);
  private readonly toast = inject(ToastService);

  protected readonly phases = rxResource({ stream: () => this.api.phases() });
  protected readonly list = computed(() => this.phases.value() ?? []);
  protected readonly newName = signal('');
  protected readonly busy = signal(false);
  protected readonly loadError = computed(() =>
    problemMessage(this.phases.error(), 'Phases could not be loaded.'),
  );

  protected add(): void {
    const name = this.newName().trim();
    if (!name || this.busy()) {
      return;
    }
    this.run(this.api.addPhase(name), (phase) => {
      this.newName.set('');
      this.phases.update((list) => [...(list ?? []), phase]);
      this.toast.success(`${phase.name} added.`);
    });
  }

  protected rename(phase: Phase, input: HTMLInputElement): void {
    const name = input.value.trim();
    if (!name || name === phase.name) {
      input.value = phase.name;
      return;
    }
    this.run(
      this.api.updatePhase(phase, name, phase.isActive),
      (saved) => {
        this.replace(saved);
        this.toast.success(`Renamed to ${saved.name}.`);
      },
      () => (input.value = phase.name),
    );
  }

  protected setActive(phase: Phase, isActive: boolean): void {
    this.run(
      this.api.updatePhase(phase, phase.name, isActive),
      (saved) => {
        this.replace(saved);
        this.toast.success(
          isActive
            ? `${saved.name} is offered again.`
            : `${saved.name} is hidden from new sprints.`,
        );
      },
      () => this.phases.reload(),
    );
  }

  protected move(index: number, step: -1 | 1): void {
    const order = [...this.list()];
    [order[index], order[index + step]] = [order[index + step], order[index]];
    this.phases.set(order); // show the move at once; the answer brings the new versions
    this.run(
      this.api.reorderPhases(order),
      (saved) => this.phases.set(saved),
      () => this.phases.reload(),
    );
  }

  private replace(saved: Phase): void {
    this.phases.update((list) => (list ?? []).map((p) => (p.id === saved.id ? saved : p)));
  }

  private run<T>(request: Observable<T>, done: (value: T) => void, failed?: () => void): void {
    this.busy.set(true);
    request.subscribe({
      next: (value) => {
        this.busy.set(false);
        done(value);
      },
      error: () => {
        this.busy.set(false);
        failed?.();
      },
    });
  }
}
