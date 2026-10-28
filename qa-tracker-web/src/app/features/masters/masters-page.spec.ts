import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { provideFakeAuth, testUser } from '../../../testing/fake-auth';
import { Client, Project } from '../../shared/data/masters.api';
import { MastersPage } from './masters-page';

const pardi: Client = {
  id: 1,
  name: 'Pardi Gears',
  contactPerson: null,
  email: null,
  phone: null,
  isActive: true,
  projectCount: 1,
  rowVersion: 'AAAAAAAAB9E=',
};

const erp: Project = {
  id: 4,
  clientId: 1,
  clientName: 'Pardi Gears',
  name: 'ERP',
  key: 'PGERP',
  requireLiveVerification: false,
  isActive: true,
  moduleCount: 2,
  environmentCount: 1,
  memberCount: 3,
  hasIssues: false,
  rowVersion: 'AAAAAAAAB9I=',
};

describe('MastersPage', () => {
  let http: HttpTestingController;
  let queryParams: BehaviorSubject<ReturnType<typeof convertToParamMap>>;

  function render(params: Record<string, string> = {}) {
    queryParams = new BehaviorSubject(convertToParamMap(params));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeAuth(testUser('Tester')),
        {
          provide: ActivatedRoute,
          useValue: {
            queryParamMap: queryParams,
            snapshot: { queryParamMap: queryParams.value },
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(MastersPage);
    TestBed.tick();
    return fixture;
  }

  it('shows the empty state when there are no clients', async () => {
    const fixture = render();
    http.expectOne((r) => r.url === '/api/clients').flush([]);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No clients yet');
  });

  it('shows an error state when clients cannot load', async () => {
    const fixture = render();
    http
      .expectOne((r) => r.url === '/api/clients')
      .flush({ detail: 'Database offline.' }, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(text).toContain('Could not load clients');
    expect(text).toContain('Database offline.');
  });

  it('opens a deep-linked project in the tree and the detail panel', async () => {
    const fixture = render({ client: '1', project: '4', tab: 'members' });
    http.expectOne((r) => r.url === '/api/clients').flush([pardi]);
    http.expectOne((r) => r.url === '/api/clients/1/projects').flush([erp]);
    TestBed.tick();
    http.expectOne((r) => r.url === '/api/projects/4').flush(erp);
    TestBed.tick();
    // The Members tab is selected, so its data loads once the tab group renders.
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
    http.expectOne((r) => r.url === '/api/projects/4/members').flush([]);
    http.expectOne((r) => r.url === '/api/lookups/users').flush([]);
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Pardi Gears');
    expect(text).toContain('PGERP');
    expect(text).toContain('Members (3)');
  });
});
