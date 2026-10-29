import { ParamMap, Params } from '@angular/router';
import { Board, BoardCard, BoardColumn, BoardQuery } from '../../shared/data/board.api';
import { AllowedAction } from '../../shared/data/issues.api';
import {
  ISSUE_STATUS_META,
  IssueStatus,
  SEVERITIES,
  Severity,
} from '../../shared/models/issue-enums';
import { Role } from '../../shared/models/role';

/** Board state – in the URL so a board can be bookmarked and shared (like the issue list, ADR-025). */
export interface BoardFilters {
  readonly clientId: number | null;
  readonly projectId: number | null;
  readonly sprintId: number | null;
  /** 'me', 'none' (unassigned) or a user id. */
  readonly who: 'me' | 'none' | number | null;
  readonly severity: readonly Severity[];
  readonly q: string;
  /** Show the Closed and Rejected columns. */
  readonly done: boolean;
}

/** Hidden unless `done` is on – they only grow. */
export const DONE_COLUMNS: readonly IssueStatus[] = ['Closed', 'Rejected'];

function id(value: string | null): number | null {
  const n = Number(value);
  return Number.isInteger(n) && n > 0 ? n : null;
}

export function boardFiltersFromParams(params: ParamMap): BoardFilters {
  const who = params.get('who');
  return {
    clientId: id(params.get('client')),
    projectId: id(params.get('project')),
    sprintId: id(params.get('sprint')),
    who: who === 'me' || who === 'none' ? who : id(who),
    severity: (params.get('severity') ?? '')
      .split(',')
      .filter((s): s is Severity => (SEVERITIES as readonly string[]).includes(s)),
    q: params.get('q') ?? '',
    done: params.get('done') === '1',
  };
}

/** Query params for the router; defaults are left out. */
export function boardFiltersToParams(f: BoardFilters): Params {
  return {
    client: f.clientId,
    project: f.projectId,
    sprint: f.sprintId,
    who: f.who,
    severity: f.severity.length ? f.severity.join(',') : null,
    q: f.q.trim() || null,
    done: f.done ? '1' : null,
  };
}

export function boardQuery(f: BoardFilters & { readonly projectId: number }): BoardQuery {
  return {
    projectId: f.projectId,
    sprintId: f.sprintId,
    mine: f.who === 'me',
    unassigned: f.who === 'none',
    assigneeId: typeof f.who === 'number' ? f.who : null,
    severity: f.severity,
    q: f.q,
  };
}

/** The workflow action that drops the card on this column, if the user may take it. */
export function moveFor(card: BoardCard, target: IssueStatus): AllowedAction | undefined {
  return card.moves.find((m) => m.target === target);
}

/** Why a drop is refused – in the words of the workflow, with the columns that would work. */
export function refusal(card: BoardCard, target: IssueStatus, role: Role | null): string {
  if (card.sprintStatus === 'Closed') {
    return `${card.key} belongs to a closed sprint – its issues are read-only.`;
  }
  if (card.sprintStatus === 'Testing' && role === 'Developer') {
    return `Testing of ${card.sprintName} is still in progress. You can move ${card.key} once the tester marks testing completed.`;
  }
  const from = ISSUE_STATUS_META[card.status].label;
  const to = ISSUE_STATUS_META[target].label;
  const options = [...new Set(card.moves.map((m) => ISSUE_STATUS_META[m.target].label))];
  return options.length
    ? `${card.key} can't go from ${from} to ${to}. It can go to ${joinOr(options)}.`
    : `${card.key} can't go from ${from} to ${to} – there is nothing you can move it to right now.`;
}

function joinOr(items: readonly string[]): string {
  return items.length < 2
    ? items.join('')
    : `${items.slice(0, -1).join(', ')} or ${items[items.length - 1]}`;
}

/** A move in flight: the card is shown in its new column until the API answers. */
export interface PendingMove {
  readonly key: string;
  readonly to: IssueStatus;
}

export function visibleColumns(
  board: Board,
  done: boolean,
  pending: PendingMove | null,
): BoardColumn[] {
  const columns = board.columns.filter((c) => done || !DONE_COLUMNS.includes(c.status));
  if (!pending) {
    return columns;
  }
  const card = board.columns.flatMap((c) => c.cards).find((c) => c.key === pending.key);
  if (!card) {
    return columns;
  }
  return columns.map((column) => {
    if (column.status === card.status) {
      return {
        ...column,
        count: column.count - 1,
        cards: column.cards.filter((c) => c.key !== card.key),
      };
    }
    if (column.status === pending.to) {
      return {
        ...column,
        count: column.count + 1,
        cards: [{ ...card, moves: [] }, ...column.cards],
      };
    }
    return column;
  });
}
