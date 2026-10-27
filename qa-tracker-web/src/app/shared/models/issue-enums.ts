/** Issue enums mirrored from the API (plan 7.1 / 7.2) with display metadata. Colours are token names, never hex. */

export const ISSUE_STATUSES = [
  'New',
  'Assigned',
  'InProgress',
  'NeedInfo',
  'OnHold',
  'Disputed',
  'Fixed',
  'Verified',
  'Closed',
  'Reopened',
  'Rejected',
] as const;
export type IssueStatus = (typeof ISSUE_STATUSES)[number];

export const SEVERITIES = ['Critical', 'High', 'Medium', 'Low', 'NotAssessed'] as const;
export type Severity = (typeof SEVERITIES)[number];

export const PRIORITIES = ['High', 'Medium', 'Low'] as const;
export type Priority = (typeof PRIORITIES)[number];

export interface EnumMeta {
  readonly label: string;
  /** CSS custom property holding the colour, e.g. `--qa-status-fixed`. */
  readonly colorToken: string;
}

export const ISSUE_STATUS_META: Readonly<Record<IssueStatus, EnumMeta>> = {
  New: { label: 'New', colorToken: '--qa-status-new' },
  Assigned: { label: 'Assigned', colorToken: '--qa-status-assigned' },
  InProgress: { label: 'In Progress', colorToken: '--qa-status-in-progress' },
  NeedInfo: { label: 'Need Info', colorToken: '--qa-status-need-info' },
  OnHold: { label: 'On Hold', colorToken: '--qa-status-on-hold' },
  Disputed: { label: 'Disputed', colorToken: '--qa-status-disputed' },
  Fixed: { label: 'Fixed', colorToken: '--qa-status-fixed' },
  Verified: { label: 'Verified', colorToken: '--qa-status-verified' },
  Closed: { label: 'Closed', colorToken: '--qa-status-closed' },
  Reopened: { label: 'Reopened', colorToken: '--qa-status-reopened' },
  Rejected: { label: 'Rejected', colorToken: '--qa-status-rejected' },
};

export const SEVERITY_META: Readonly<Record<Severity, EnumMeta>> = {
  Critical: { label: 'Critical', colorToken: '--qa-severity-critical' },
  High: { label: 'High', colorToken: '--qa-severity-high' },
  Medium: { label: 'Medium', colorToken: '--qa-severity-medium' },
  Low: { label: 'Low', colorToken: '--qa-severity-low' },
  NotAssessed: { label: 'Not assessed', colorToken: '--qa-severity-not-assessed' },
};

export const PRIORITY_META: Readonly<Record<Priority, EnumMeta>> = {
  High: { label: 'High', colorToken: '--qa-severity-high' },
  Medium: { label: 'Medium', colorToken: '--qa-severity-medium' },
  Low: { label: 'Low', colorToken: '--qa-severity-low' },
};
