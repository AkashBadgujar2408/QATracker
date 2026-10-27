import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideFakeAuth, testUser } from '../testing/fake-auth';
import { routes } from './app.routes';

describe('repro', () => {
  it('admin opens users page', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideFakeAuth(testUser('Admin')),
      ],
    });
    const harness = await RouterTestingHarness.create();
    const nav = harness.navigateByUrl('/admin/users');
    await new Promise((r) => setTimeout(r, 200));
    const http = TestBed.inject(HttpTestingController);
    http.match(() => true).forEach((r) => r.flush([]));
    await nav;
    expect(harness.routeNativeElement?.textContent).toContain('Users');
  }, 10000);
});
