import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { ConfirmService } from './confirm.service';
import { ToastService } from './toast.service';

describe('ConfirmService', () => {
  function setup(result: boolean | undefined) {
    const open = vi.fn().mockReturnValue({ afterClosed: () => of(result) });
    TestBed.configureTestingModule({ providers: [{ provide: MatDialog, useValue: { open } }] });
    return { service: TestBed.inject(ConfirmService), open };
  }

  it('resolves true when confirmed', async () => {
    const { service } = setup(true);
    expect(await service.ask({ title: 'T', message: 'M' })).toBe(true);
  });

  it('resolves false when cancelled or dismissed', async () => {
    const { service } = setup(undefined);
    expect(await service.ask({ title: 'T', message: 'M' })).toBe(false);
  });

  it('focuses Cancel first for destructive actions', async () => {
    const { service, open } = setup(false);
    await service.ask({ title: 'Delete?', message: 'M', danger: true });
    expect(open.mock.calls[0][1].autoFocus).toBe('.qa-confirm-cancel');
  });
});

describe('ToastService', () => {
  it('shows success toasts for 3 seconds with the success style', () => {
    const open = vi.fn();
    TestBed.configureTestingModule({ providers: [{ provide: MatSnackBar, useValue: { open } }] });

    TestBed.inject(ToastService).success('Saved');

    expect(open).toHaveBeenCalledWith(
      'Saved',
      undefined,
      expect.objectContaining({ duration: 3000, panelClass: ['qa-toast', 'qa-toast-success'] }),
    );
  });
});
