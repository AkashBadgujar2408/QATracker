import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { EntityPicker, PickerItem, QuickAdd } from './entity-picker';

const MODULES: PickerItem[] = [
  { id: 1, label: 'Sales' },
  { id: 2, label: 'Sales Return' },
  { id: 3, label: 'Inventory' },
];

@Component({
  imports: [EntityPicker, ReactiveFormsModule],
  template: `
    <app-entity-picker
      label="Module"
      noun="module"
      [items]="items()"
      [quickAdd]="quickAdd"
      [formControl]="control"
      (created)="created.set($event)"
    />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class Host {
  readonly items = signal(MODULES);
  readonly control = new FormControl<number | null>(null, Validators.required);
  readonly created = signal<PickerItem | null>(null);
  quickAdd: QuickAdd = vi.fn(async (text: string) => ({ id: 9, label: text }));
}

describe('EntityPicker', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const input = (fixture.nativeElement as HTMLElement).querySelector('input')!;
    return { fixture, host: fixture.componentInstance, input };
  }

  async function type(
    fixture: { whenStable(): Promise<unknown> },
    input: HTMLInputElement,
    text: string,
  ) {
    input.focus();
    input.dispatchEvent(new Event('focusin'));
    input.value = text;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  }

  function options(): HTMLElement[] {
    return Array.from(document.querySelectorAll<HTMLElement>('mat-option'));
  }

  it('filters the items by the typed text', async () => {
    const { fixture, input } = await render();
    await type(fixture, input, 'sal');
    expect(options().map((o) => o.textContent?.trim())).toEqual([
      'Sales',
      'Sales Return',
      expect.stringContaining('Add “sal” as new module'),
    ]);
  });

  it('does not offer to add a name that already exists', async () => {
    const { fixture, input } = await render();
    await type(fixture, input, 'inventory');
    expect(options().map((o) => o.textContent?.trim())).toEqual(['Inventory']);
  });

  it('selects an option into the form control and shows its name', async () => {
    const { fixture, host, input } = await render();
    await type(fixture, input, 'inv');
    options()[0].click();
    await fixture.whenStable();
    expect(host.control.value).toBe(3);
    expect(input.value).toBe('Inventory');
  });

  it('quick-adds the typed name, selects it and emits created', async () => {
    const { fixture, host, input } = await render();
    await type(fixture, input, 'Kaizen');
    options()
      .find((o) => o.textContent?.includes('Add'))!
      .click();
    await fixture.whenStable();
    expect(host.quickAdd).toHaveBeenCalledWith('Kaizen');
    expect(host.control.value).toBe(9);
    expect(host.created()).toEqual({ id: 9, label: 'Kaizen' });
    expect(input.value).toBe('Kaizen');
  });

  it('shows the label of a value written by the form', async () => {
    const { fixture, host, input } = await render();
    host.control.setValue(2);
    await fixture.whenStable();
    expect(input.value).toBe('Sales Return');
  });

  it('shows the required error once touched', async () => {
    const { fixture, host } = await render();
    host.control.markAsTouched();
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Module is required.');
  });
});
