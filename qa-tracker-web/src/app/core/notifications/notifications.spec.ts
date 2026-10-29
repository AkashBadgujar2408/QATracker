import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideFakeAuth, testUser } from '../../../testing/fake-auth';
import { FakeRealtime } from '../../../testing/fake-realtime';
import { AppNotification, notificationLink } from '../../shared/data/notifications.api';
import { NotificationsPage, groupByDay } from '../../features/notifications/notifications-page';
import { NotificationBell } from './notification-bell';
import { NotificationStore } from './notification.store';

function note(overrides: Partial<AppNotification> = {}): AppNotification {
  return {
    id: 1,
    type: 'Assigned',
    title: 'John assigned PGERP-4 to you',
    body: 'Invoice total is wrong',
    issueKey: 'PGERP-4',
    sprintId: 2,
    projectId: 1,
    isRead: false,
    createdAt: '2026-10-29T05:00:00Z',
    ...overrides,
  };
}

function setup() {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      provideFakeAuth(testUser('Developer')),
    ],
  });
  return {
    http: TestBed.inject(HttpTestingController),
    realtime: TestBed.inject(FakeRealtime),
  };
}

describe('notification helpers', () => {
  it('links to the issue, else to the sprint issues of the project', () => {
    expect(notificationLink(note())).toEqual({ path: '/issues/PGERP-4' });
    expect(notificationLink(note({ issueKey: null }))).toEqual({
      path: '/issues',
      query: { project: 1, sprint: 2 },
    });
    expect(notificationLink(note({ issueKey: null, projectId: null }))).toBeNull();
  });

  it('groups by IST day: Today, Yesterday, then the date', () => {
    const now = new Date('2026-10-29T10:00:00Z'); // 15:30 IST
    const groups = groupByDay(
      [
        note({ id: 4, createdAt: '2026-10-29T09:00:00Z' }),
        note({ id: 3, createdAt: '2026-10-28T19:00:00Z' }), // 29 Oct 00:30 IST
        note({ id: 2, createdAt: '2026-10-28T10:00:00Z' }),
        note({ id: 1, createdAt: '2026-10-25T10:00:00Z' }),
      ],
      now,
    );
    expect(groups.map((g) => [g.label, g.items.map((i) => i.id)])).toEqual([
      ['Today', [4, 3]],
      ['Yesterday', [2]],
      ['25/Oct/2026', [1]],
    ]);
  });
});

describe('NotificationStore', () => {
  it('loads the history, counts live arrivals and marks read optimistically', () => {
    const { http, realtime } = setup();
    const store = TestBed.inject(NotificationStore);
    TestBed.tick();
    http
      .expectOne((r) => r.url === '/api/notifications')
      .flush({ items: [note({ id: 1 })], unreadCount: 1, hasMore: false });
    expect(store.badge()).toBe('1');

    realtime.notificationCreated$.next(note({ id: 2, type: 'Reopened' }));
    expect(store.recent().map((n) => n.id)).toEqual([2, 1]);
    expect(store.unreadCount()).toBe(2);
    expect(store.arrivals()).toBe(1);

    store.markRead([2]);
    expect(store.unreadCount()).toBe(1);
    expect(store.recent()[0].isRead).toBe(true);
    const request = http.expectOne('/api/notifications/read');
    expect(request.request.body).toEqual({ ids: [2], read: true });
    request.flush({ unreadCount: 1 });

    store.markAllRead();
    expect(store.badge()).toBeNull();
    expect(http.expectOne('/api/notifications/read').request.body).toEqual({ all: true });
  });
});

describe('NotificationBell', () => {
  it('shows the badge, opens the panel and opens a notification', async () => {
    const { http } = setup();
    const fixture = TestBed.createComponent(NotificationBell);
    fixture.detectChanges();
    http
      .expectOne((r) => r.url === '/api/notifications')
      .flush({
        items: [
          note({ id: 5 }),
          note({ id: 4, isRead: true, type: 'Comment', title: 'Emily commented' }),
        ],
        unreadCount: 1,
        hasMore: false,
      });
    await fixture.whenStable();
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(button.getAttribute('aria-label')).toBe('Notifications, 1 unread');
    expect(button.textContent).toContain('1');

    button.click();
    await fixture.whenStable();
    const panel = document.querySelector('[role="dialog"][aria-label="Notifications"]');
    expect(panel?.textContent).toContain('John assigned PGERP-4 to you');
    expect(panel?.textContent).toContain('Emily commented');

    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    (panel?.querySelector('.qa-note-open') as HTMLButtonElement).click();
    expect(http.expectOne('/api/notifications/read').request.body).toEqual({
      ids: [5],
      read: true,
    });
    expect(navigate).toHaveBeenCalledWith(['/issues/PGERP-4'], { queryParams: undefined });
  });
});

describe('NotificationsPage', () => {
  it('pages the full history, prepends live ones and mirrors read changes', async () => {
    const { http, realtime } = setup();
    const fixture = TestBed.createComponent(NotificationsPage);
    fixture.detectChanges();
    const store = TestBed.inject(NotificationStore);
    for (const request of http.match((r) => r.url === '/api/notifications')) {
      const size = request.request.params.get('size');
      request.flush(
        size === '30'
          ? {
              items: [note({ id: 9 }), note({ id: 8, isRead: true })],
              unreadCount: 1,
              hasMore: true,
            }
          : { items: [note({ id: 9 })], unreadCount: 1, hasMore: false },
      );
    }
    await fixture.whenStable();
    const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text()).toContain('Show older');

    (
      [...fixture.nativeElement.querySelectorAll('button')].find((b: HTMLButtonElement) =>
        b.textContent?.includes('Show older'),
      ) as HTMLButtonElement
    ).click();
    const older = http.expectOne((r) => r.params.get('before') === '8');
    older.flush({
      items: [note({ id: 3, title: 'Old one', isRead: true })],
      unreadCount: 1,
      hasMore: false,
    });
    await fixture.whenStable();
    expect(text()).toContain('Old one');
    expect(text()).not.toContain('Show older');

    realtime.notificationCreated$.next(note({ id: 12, title: 'Fresh one' }));
    await fixture.whenStable();
    expect(text()).toContain('Fresh one');

    store.markRead([9]);
    http.expectOne('/api/notifications/read').flush({ unreadCount: 1 });
    await fixture.whenStable();
    const unreadRows = fixture.nativeElement.querySelectorAll('.qa-note-unread');
    expect(unreadRows.length).toBe(1); // only the live one is still unread
  });
});
