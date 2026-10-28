import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MasterDialog, MasterDialogData } from './master-dialog';

describe('MasterDialog', () => {
  let http: HttpTestingController;
  const close = vi.fn();

  function render(data: MasterDialogData) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.createComponent(MasterDialog);
  }

  async function settle(fixture: { whenStable(): Promise<unknown> }) {
    await new Promise((resolve) => setTimeout(resolve, 300)); // name debounce
    await fixture.whenStable();
  }

  beforeEach(() => close.mockReset());

  it('blocks a duplicate name and offers the existing item', async () => {
    const fixture = render({
      kind: 'Module',
      name: 'sales return',
      parent: { id: 7, name: 'ERP' },
    });
    await settle(fixture);

    const similar = http.expectOne((r) => r.url === '/api/lookups/similar');
    expect(similar.request.params.get('kind')).toBe('Module');
    expect(similar.request.params.get('parentId')).toBe('7');
    similar.flush([{ id: 3, name: 'Sales Return', exact: true, isActive: true }]);
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Sales Return already exists.');
    const submit = host.querySelector<HTMLButtonElement>('button[type=submit]')!;
    expect(submit.disabled).toBe(true);

    Array.from(host.querySelectorAll('button'))
      .find((b) => b.textContent?.includes('Use it'))!
      .click();
    expect(close).toHaveBeenCalledWith({ id: 3, name: 'Sales Return', isNew: false });
  });

  it('warns about similar names but still allows creating', async () => {
    const fixture = render({ kind: 'Client', name: 'Pardi Gear' });
    await settle(fixture);
    http
      .expectOne((r) => r.url === '/api/lookups/similar')
      .flush([{ id: 1, name: 'Pardi Gears', exact: false, isActive: true }]);
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Similar client names already exist');
    expect(host.textContent).toContain('Pardi Gears');

    host.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    const create = http.expectOne((r) => r.url === '/api/clients' && r.method === 'POST');
    expect(create.request.body).toEqual({ name: 'Pardi Gear' });
    create.flush({ id: 5, name: 'Pardi Gear' });
    expect(close).toHaveBeenCalledWith({ id: 5, name: 'Pardi Gear', isNew: true });
  });

  it('suggests a project key from the name', async () => {
    const fixture = render({
      kind: 'Project',
      name: 'ERP',
      parent: { id: 2, name: 'Pardi Gears' },
    });
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/lookups/similar').flush([]);
    const suggestion = http.expectOne((r) => r.url === '/api/lookups/project-key');
    expect(suggestion.request.params.get('clientId')).toBe('2');
    suggestion.flush({ key: 'PGERP' });
    await fixture.whenStable();

    const key = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      'input[formcontrolname=key]',
    )!;
    expect(key.value).toBe('PGERP');
  });
});
