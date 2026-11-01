import { Role } from '../../shared/models/role';

/** One row of the shortcuts help: the keys to press (each a <kbd>) and what they do. */
export interface Shortcut {
  /** Keys pressed one after the other ("g", "i") – a "+" inside one entry means together ("Ctrl+Enter"). */
  readonly keys: readonly string[];
  readonly description: string;
}

export interface ShortcutGroup {
  readonly title: string;
  readonly shortcuts: readonly Shortcut[];
}

/** "g" then a letter: go to a page (only pages the role may open). */
export interface GoTarget {
  readonly key: string;
  readonly label: string;
  readonly link: string;
  readonly roles?: readonly Role[];
}

export const GO_TARGETS: readonly GoTarget[] = [
  { key: 'h', label: 'Home', link: '/home' },
  { key: 't', label: 'Testing', link: '/testing', roles: ['Tester', 'Admin'] },
  { key: 'w', label: 'My work', link: '/my-work', roles: ['Developer', 'Admin'] },
  { key: 'i', label: 'Issues', link: '/issues' },
  { key: 'b', label: 'Board', link: '/board' },
  { key: 'r', label: 'Retest queue', link: '/retest-queue', roles: ['Tester', 'Admin'] },
  { key: 's', label: 'Sprints', link: '/sprints' },
  { key: 'n', label: 'Notifications', link: '/notifications' },
];

export function goTargetsFor(role: Role | null): GoTarget[] {
  return GO_TARGETS.filter((t) => !t.roles || (role !== null && t.roles.includes(role)));
}

/** Everything the help dialog lists for a role – page-specific keys included, so they can be found. */
export function shortcutGroupsFor(role: Role | null): ShortcutGroup[] {
  const tester = role === 'Tester' || role === 'Admin';
  return [
    {
      title: 'Anywhere',
      shortcuts: [
        { keys: ['?'], description: 'Show keyboard shortcuts' },
        { keys: ['/'], description: 'Search issues (key or text)' },
        { keys: ['Esc'], description: 'Close a dialog or menu' },
      ],
    },
    {
      title: 'Go to',
      shortcuts: goTargetsFor(role).map((t) => ({ keys: ['g', t.key], description: t.label })),
    },
    {
      title: 'Forms and comments',
      shortcuts: [
        ...(tester
          ? [{ keys: ['n'], description: 'New issue (Testing screen, sprint in testing)' }]
          : []),
        { keys: ['Ctrl+Enter'], description: 'Save the issue, send the comment or run the action' },
        { keys: ['Ctrl+V'], description: 'Paste a screenshot into the issue or comment' },
      ],
    },
    {
      title: 'Screenshots',
      shortcuts: [
        { keys: ['←', '→'], description: 'Previous / next picture' },
        { keys: ['Esc'], description: 'Close the viewer' },
      ],
    },
  ];
}

/** Typing into a field (or a rich-text editor) – single-key shortcuts must not fire then. */
export function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  const tag = target.tagName;
  return (
    tag === 'INPUT' ||
    tag === 'TEXTAREA' ||
    tag === 'SELECT' ||
    target.isContentEditable ||
    target.closest(
      '[contenteditable="true"], [role="listbox"], [role="combobox"], .mat-mdc-select',
    ) !== null
  );
}

/** "ORD-12" (any case, spaces around) → "ORD-12"; anything else → null. */
export function issueKeyFrom(text: string): string | null {
  const match = /^\s*([a-z][a-z0-9]{0,9}-\d{1,7})\s*$/i.exec(text);
  return match ? match[1].toUpperCase() : null;
}
