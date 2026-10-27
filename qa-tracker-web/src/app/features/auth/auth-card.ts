import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { IssueStatus } from '../../shared/models/issue-enums';
import { Logo } from '../../shared/ui/logo/logo';
import { StatusPill } from '../../shared/ui/status-pill/status-pill';

interface JourneyStep {
  readonly status: IssueStatus;
  readonly who: string;
  readonly what: string;
  readonly when: string;
}

/**
 * Sign-in / password pages (outside the app shell): an ink brand panel that shows what the product does –
 * one issue travelling from report to verification – next to the form. The panel hides on small screens.
 */
@Component({
  selector: 'app-auth-card',
  imports: [Logo, StatusPill],
  template: `
    <aside class="brand-panel hidden flex-col justify-between p-12 lg:flex" aria-hidden="true">
      <div class="flex items-center gap-3">
        <app-logo [size]="36" />
        <span class="text-lg font-semibold tracking-tight text-slate-50">QA Tracker</span>
      </div>

      <div class="max-w-md">
        <p class="m-0 mb-8 text-2xl font-semibold leading-snug tracking-tight text-slate-50">
          Log the issue. Hand it to a developer. Retest the fix on the right URL.
        </p>

        <div class="journey">
          <div class="mb-4 flex items-baseline gap-3">
            <span class="font-semibold whitespace-nowrap text-slate-50 tabular-nums"
              >PGERP-142</span
            >
            <span class="truncate text-sm text-slate-300"
              >Quantity accepts negative values on paste</span
            >
          </div>
          <ol class="m-0 list-none p-0">
            @for (step of journey; track step.when) {
              <li class="step">
                <app-status-pill [status]="step.status" class="pill" />
                <span class="text-sm text-slate-200">
                  <strong class="font-medium text-slate-50">{{ step.who }}</strong> {{ step.what }}
                </span>
                <span class="ml-auto shrink-0 text-xs text-slate-400 tabular-nums">{{
                  step.when
                }}</span>
              </li>
            }
          </ol>
        </div>
      </div>

      <p class="m-0 text-sm text-slate-400">Clients, projects and testing sprints in one place.</p>
    </aside>

    <main class="flex flex-1 items-center justify-center bg-bg px-6 py-12">
      <div class="w-full max-w-sm">
        <div class="mb-8 flex items-center gap-3 lg:hidden">
          <app-logo [size]="32" />
          <span class="text-lg font-semibold tracking-tight text-text">QA Tracker</span>
        </div>
        <h1 class="m-0 mb-2 text-xl font-semibold tracking-tight text-text">{{ heading() }}</h1>
        @if (subheading()) {
          <p class="mt-0 mb-8 text-base text-muted">{{ subheading() }}</p>
        }
        <ng-content />
      </div>
    </main>
  `,
  styles: `
    :host {
      display: flex;
      min-height: 100dvh;
    }
    .brand-panel {
      width: min(44vw, 560px);
      background: var(--qa-sidebar-bg);
      border-right: 1px solid var(--qa-sidebar-border);
    }
    .journey {
      padding: 20px;
      border: 1px solid var(--qa-slate-700);
      border-radius: var(--qa-radius-card);
      background: var(--qa-slate-800);
    }
    .step {
      position: relative;
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 0 10px 0;
    }
    /* Connector between the steps of the journey. */
    .step + .step::before {
      content: '';
      position: absolute;
      left: 18px;
      top: -6px;
      height: 12px;
      border-left: 2px dotted var(--qa-slate-600);
    }
    .pill {
      width: 96px;
      justify-content: center;
      --qa-text: var(--qa-slate-50);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthCard {
  readonly heading = input.required<string>();
  readonly subheading = input<string>();

  protected readonly journey: readonly JourneyStep[] = [
    { status: 'New', who: 'John', what: 'reported it on UAT 1', when: '27/Oct 10:15' },
    { status: 'InProgress', who: 'Emily', what: 'picked it up', when: '28/Oct 11:30' },
    { status: 'Fixed', who: 'Emily', what: 'deployed the fix on UAT 1', when: '28/Oct 18:10' },
    { status: 'Verified', who: 'John', what: 'retested and verified', when: '29/Oct 17:05' },
  ];
}
