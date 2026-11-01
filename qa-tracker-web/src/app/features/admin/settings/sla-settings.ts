import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { problemMessage } from '../../../core/api/api';
import { ToastService } from '../../../core/feedback/toast.service';
import { SEVERITY_META, Severity } from '../../../shared/models/issue-enums';
import { EmptyState } from '../../../shared/ui/empty-state/empty-state';
import { SeverityBadge } from '../../../shared/ui/severity-badge/severity-badge';
import { SkeletonLines } from '../../../shared/ui/skeleton/skeleton';
import { MAX_SLA_HOURS, SettingsApi, SlaPolicy, describeWorkingHours } from './settings.api';

interface SlaRow {
  readonly policy: SlaPolicy;
  readonly hours: number | null;
  readonly changed: boolean;
  readonly valid: boolean;
  /** Bar length in % of the longest window. */
  readonly share: number;
}

/**
 * SLA per severity (plan 8.4): working hours from "testing completed" to the due date. Each window is drawn to one
 * scale so the admin sees how the severities relate while editing.
 */
@Component({
  selector: 'app-sla-settings',
  imports: [MatButton, SeverityBadge, SkeletonLines, EmptyState],
  template: `
    @if (sla.isLoading() && !sla.hasValue()) {
      <app-skeleton-lines [lines]="5" />
    } @else if (sla.error() && !sla.hasValue()) {
      <app-empty-state kind="error" title="The SLA could not be loaded" [message]="loadError()">
        <button matButton="outlined" type="button" (click)="sla.reload()">Try again</button>
      </app-empty-state>
    } @else {
      <p class="m-0 mb-5 max-w-3xl text-muted">
        When testing of a sprint is completed, every open issue gets a due date this many working
        hours later (8 hours a day; Sundays and holidays don't count). A change applies to due dates
        set from now on – existing ones stay.
      </p>

      <form (submit)="$event.preventDefault(); save()">
        <ul class="m-0 list-none p-0">
          @for (r of rows(); track r.policy.severity) {
            <li class="qa-sla-row">
              <app-severity-badge class="qa-sla-badge" [severity]="r.policy.severity" />
              <label class="qa-sla-hours">
                <input
                  type="number"
                  inputmode="numeric"
                  min="1"
                  [max]="maxHours"
                  step="1"
                  [value]="r.hours ?? ''"
                  [attr.aria-label]="'Working hours for ' + label(r.policy.severity)"
                  [attr.aria-invalid]="!r.valid"
                  [class.qa-invalid]="!r.valid"
                  (input)="edit(r.policy.severity, $any($event.target).value)"
                />
                <span class="text-sm text-muted">h</span>
              </label>
              <div class="qa-sla-window">
                <div class="qa-sla-track" aria-hidden="true">
                  <div
                    class="qa-sla-bar"
                    [class.qa-sla-bar-changed]="r.changed"
                    [style.width.%]="r.share"
                    [style.background]="'var(' + tone(r.policy.severity) + ')'"
                  ></div>
                </div>
                <span
                  class="text-sm"
                  [class.text-muted]="!r.changed"
                  [class.font-medium]="r.changed"
                >
                  @if (r.valid && r.hours !== null) {
                    {{ describe(r.hours) }}
                  } @else {
                    <span class="text-danger">1 to {{ maxHours }} hours</span>
                  }
                </span>
              </div>
            </li>
          }
        </ul>

        <div class="mt-5 flex flex-wrap items-center gap-3">
          <button matButton="filled" type="submit" [disabled]="!dirty() || !valid() || saving()">
            {{ saving() ? 'Saving…' : 'Save SLA' }}
          </button>
          @if (dirty()) {
            <button matButton type="button" [disabled]="saving()" (click)="edits.set({})">
              Undo changes
            </button>
          }
        </div>
      </form>
    }
  `,
  styles: `
    .qa-sla-row {
      display: grid;
      grid-template-columns: 8.5rem 7rem minmax(0, 1fr);
      align-items: center;
      gap: 16px;
      padding: 12px 0;
    }
    .qa-sla-row + .qa-sla-row {
      border-top: 1px solid var(--qa-border);
    }
    .qa-sla-hours {
      display: flex;
      align-items: center;
      gap: 6px;
    }
    .qa-sla-hours input {
      width: 5.5rem;
      padding: 6px 8px;
      border: 1px solid var(--qa-border-strong);
      border-radius: var(--qa-radius-control);
      background: var(--qa-surface);
      color: var(--qa-text);
      font: inherit;
      text-align: right;
      font-variant-numeric: tabular-nums;
    }
    .qa-sla-hours input:focus {
      outline: 2px solid var(--qa-focus-ring);
      outline-offset: 1px;
    }
    .qa-sla-hours input.qa-invalid {
      border-color: var(--qa-severity-critical);
    }
    .qa-sla-window {
      display: flex;
      flex-direction: column;
      gap: 6px;
      min-width: 0;
    }
    .qa-sla-track {
      height: 10px;
      border-radius: var(--qa-radius-pill);
      background: var(--qa-surface-muted);
      overflow: hidden;
    }
    .qa-sla-bar {
      height: 100%;
      min-width: 4px;
      border-radius: var(--qa-radius-pill);
      opacity: 0.85;
      transition: width 200ms ease-out;
    }
    .qa-sla-bar-changed {
      opacity: 1;
      box-shadow: inset 0 0 0 2px color-mix(in srgb, var(--qa-text) 25%, transparent);
    }
    @media (prefers-reduced-motion: reduce) {
      .qa-sla-bar {
        transition: none;
      }
    }
    @media (max-width: 640px) {
      .qa-sla-row {
        grid-template-columns: 1fr auto;
      }
      .qa-sla-window {
        grid-column: 1 / -1;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SlaSettings {
  private readonly api = inject(SettingsApi);
  private readonly toast = inject(ToastService);

  protected readonly maxHours = MAX_SLA_HOURS;
  protected readonly sla = rxResource({ stream: () => this.api.sla() });
  protected readonly edits = signal<Partial<Record<Severity, number | null>>>({});
  protected readonly saving = signal(false);
  protected readonly loadError = computed(() =>
    problemMessage(this.sla.error(), 'The SLA could not be loaded.'),
  );

  protected readonly rows = computed<SlaRow[]>(() => {
    const edits = this.edits();
    const items = (this.sla.value() ?? []).map((policy) => {
      const edited = policy.severity in edits;
      const hours = edited ? (edits[policy.severity] ?? null) : policy.workingHours;
      const valid =
        hours !== null && Number.isInteger(hours) && hours >= 1 && hours <= MAX_SLA_HOURS;
      return { policy, hours, valid, changed: edited && hours !== policy.workingHours };
    });
    const longest = Math.max(1, ...items.map((i) => (i.valid ? i.hours! : 0)));
    return items.map((i) => ({ ...i, share: i.valid ? (i.hours! / longest) * 100 : 0 }));
  });

  protected readonly dirty = computed(() => this.rows().some((r) => r.changed));
  protected readonly valid = computed(() => this.rows().every((r) => r.valid));

  protected edit(severity: Severity, value: string): void {
    const hours = value.trim() === '' ? null : Number(value);
    this.edits.update((e) => ({ ...e, [severity]: Number.isNaN(hours) ? null : hours }));
  }

  protected save(): void {
    if (!this.dirty() || !this.valid() || this.saving()) {
      return;
    }
    const changed = this.rows()
      .filter((r) => r.changed)
      .map((r) => ({ ...r.policy, workingHours: r.hours! }));
    this.saving.set(true);
    this.api.saveSla(changed).subscribe({
      next: (policies) => {
        this.saving.set(false);
        this.edits.set({});
        this.sla.set(policies);
        this.toast.success('SLA saved. New due dates use it.');
      },
      error: () => this.saving.set(false),
    });
  }

  protected describe(hours: number): string {
    return describeWorkingHours(hours);
  }

  protected label(severity: Severity): string {
    return SEVERITY_META[severity].label;
  }

  protected tone(severity: Severity): string {
    return SEVERITY_META[severity].colorToken;
  }
}
