import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { Role } from '../../shared/models/role';
import { KeyboardShortcuts } from './keyboard-shortcuts.service';
import { goTargetsFor, isTyping, issueKeyFrom, shortcutGroupsFor } from './shortcuts';

@Component({ template: '' })
class Blank {}

function key(
  k: string,
  init: Partial<KeyboardEventInit> = {},
  target?: EventTarget,
): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key: k, cancelable: true, ...init });
  if (target) {
    Object.defineProperty(event, 'target', { value: target });
  }
  return event;
}

describe('shortcut helpers', () => {
  it('reads issue keys in any case and leaves other text alone', () => {
    expect(issueKeyFrom(' ord-12 ')).toBe('ORD-12');
    expect(issueKeyFrom('PGERP-142')).toBe('PGERP-142');
    expect(issueKeyFrom('invoice total')).toBeNull();
    expect(issueKeyFrom('ORD-')).toBeNull();
    expect(issueKeyFrom('12-ORD')).toBeNull();
  });

  it('offers only the pages the role may open', () => {
    const developer = goTargetsFor('Developer').map((t) => t.label);
    expect(developer).toContain('My work');
    expect(developer).not.toContain('Testing');
    expect(developer).not.toContain('Retest queue');
    expect(goTargetsFor('Tester').map((t) => t.label)).toContain('Retest queue');
    expect(goTargetsFor('Admin').length).toBe(8);
  });

  it('lists "n" (new issue) only for testers and admins', () => {
    const keys = (role: Role) =>
      shortcutGroupsFor(role).flatMap((g) => g.shortcuts.map((s) => s.keys.join(' ')));
    expect(keys('Tester')).toContain('n');
    expect(keys('Developer')).not.toContain('n');
    expect(keys('Developer')).toContain('?');
  });

  it('treats inputs, editors and open selects as typing', () => {
    const input = document.createElement('input');
    const editor = document.createElement('div');
    editor.setAttribute('contenteditable', 'true');
    const inside = document.createElement('span');
    editor.appendChild(inside);
    expect(isTyping(input)).toBe(true);
    expect(isTyping(inside)).toBe(true);
    expect(isTyping(document.createElement('button'))).toBe(false);
    expect(isTyping(null)).toBe(false);
  });
});

describe('KeyboardShortcuts', () => {
  const role = signal<Role | null>('Developer');
  let service: KeyboardShortcuts;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '**', component: Blank }]),
        { provide: AuthService, useValue: { role } },
      ],
    });
    service = TestBed.inject(KeyboardShortcuts);
    router = TestBed.inject(Router);
  });

  it('goes to a page with "g" then a letter, within the time window', () => {
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    expect(service.handle(key('g'), 1000)).toBe(true);
    expect(service.handle(key('i'), 1500)).toBe(true);
    expect(navigate).toHaveBeenCalledWith('/issues');

    service.handle(key('g'), 5000);
    expect(service.handle(key('i'), 9000)).toBe(false);
    expect(navigate).toHaveBeenCalledTimes(1);
  });

  it('does not open pages the role cannot use', () => {
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    service.handle(key('g'), 0);
    expect(service.handle(key('t'), 10)).toBe(false);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('ignores keys while typing or with modifiers', () => {
    const open = vi.spyOn(service, 'openHelp').mockResolvedValue();
    expect(service.handle(key('?', {}, document.createElement('textarea')))).toBe(false);
    expect(service.handle(key('?', { ctrlKey: true }))).toBe(false);
    expect(open).not.toHaveBeenCalled();

    const help = key('?');
    expect(service.handle(help)).toBe(true);
    expect(help.defaultPrevented).toBe(true);
    expect(open).toHaveBeenCalledOnce();
  });

  it('focuses the issue search with "/"', () => {
    const search = document.createElement('input');
    search.id = 'qa-global-search';
    document.body.appendChild(search);
    try {
      expect(service.handle(key('/'))).toBe(true);
      expect(document.activeElement).toBe(search);
    } finally {
      search.remove();
    }
  });
});
