import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * QA Tracker mark: an inspection lens with a check inside – "inspect, then verify".
 * Same drawing as public/favicon.svg. Colours come from the --qa-brand tokens (identical in both themes).
 */
@Component({
  selector: 'app-logo',
  template: `
    <svg
      viewBox="0 0 32 32"
      [attr.width]="size()"
      [attr.height]="size()"
      aria-hidden="true"
      focusable="false"
    >
      <rect width="32" height="32" rx="8" fill="var(--qa-brand)" />
      <circle cx="14" cy="14" r="7.5" fill="none" stroke="var(--qa-brand-ink)" stroke-width="2.6" />
      <path
        d="M10.6 14.3 L13.2 16.8 L17.6 11.8"
        fill="none"
        stroke="var(--qa-brand-ink)"
        stroke-width="2.4"
        stroke-linecap="round"
        stroke-linejoin="round"
      />
      <path
        d="M19.8 19.8 L24.6 24.6"
        stroke="var(--qa-brand-ink)"
        stroke-width="3"
        stroke-linecap="round"
      />
    </svg>
  `,
  host: { class: 'inline-flex shrink-0' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Logo {
  readonly size = input(32);
}
