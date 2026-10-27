import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { BreadcrumbService } from './breadcrumb.service';
import { LayoutService } from './layout.service';

@Component({ template: '' })
class Blank {}

describe('BreadcrumbService', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: 'dashboards',
            data: { breadcrumb: 'Dashboards' },
            children: [{ path: 'quality', component: Blank, data: { breadcrumb: 'Quality' } }],
          },
        ]),
      ],
    });
  });

  it('builds the trail from route data after navigation', async () => {
    const service = TestBed.inject(BreadcrumbService);
    await RouterTestingHarness.create('/dashboards/quality');
    expect(service.items().map((i) => i.label)).toEqual(['Dashboards', 'Quality']);
  });

  it('lets a page replace the trail', async () => {
    const service = TestBed.inject(BreadcrumbService);
    await RouterTestingHarness.create('/dashboards/quality');
    service.set([{ label: 'Pardi Gears', link: '/clients/1' }, { label: 'PGERP' }]);
    expect(service.items().map((i) => i.label)).toEqual(['Pardi Gears', 'PGERP']);
  });
});

describe('LayoutService', () => {
  beforeEach(() => localStorage.clear());

  it('remembers the collapsed sidebar', () => {
    const service = TestBed.inject(LayoutService);
    expect(service.sidebarCollapsed()).toBe(false);

    service.toggleSidebar();
    TestBed.tick();

    expect(localStorage.getItem('qa-sidebar-collapsed')).toBe('true');
  });
});
