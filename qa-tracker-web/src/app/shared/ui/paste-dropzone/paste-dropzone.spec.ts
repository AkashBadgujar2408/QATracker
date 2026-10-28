import { TestBed } from '@angular/core/testing';
import { PasteDropzone, screenshotName } from './paste-dropzone';

function file(name: string, type: string, size = 10): File {
  return new File([new Uint8Array(size)], name, { type });
}

function pasteEvent(files: File[]): ClipboardEvent {
  const event = new Event('paste', { bubbles: true, cancelable: true }) as ClipboardEvent;
  Object.defineProperty(event, 'clipboardData', { value: { files } });
  return event;
}

describe('PasteDropzone', () => {
  async function render(capturePaste = false) {
    const fixture = TestBed.createComponent(PasteDropzone);
    fixture.componentRef.setInput('capturePaste', capturePaste);
    await fixture.whenStable();
    const added: File[][] = [];
    fixture.componentInstance.added.subscribe((files) => added.push(files));
    return { fixture, dropzone: fixture.componentInstance, added };
  }

  it('accepts allowed files and rejects others with a message', async () => {
    const { fixture, dropzone, added } = await render();

    dropzone.addFiles([
      file('bug.png', 'image/png'),
      file('setup.exe', 'application/octet-stream'),
    ]);
    await fixture.whenStable();

    expect(added).toHaveLength(1);
    expect(added[0].map((f) => f.name)).toEqual(['bug.png']);
    expect(dropzone.files().map((f) => f.name)).toEqual(['bug.png']);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('setup.exe: only images');
  });

  it('rejects files over 10 MB', async () => {
    const { fixture, dropzone, added } = await render();
    dropzone.addFiles([file('huge.pdf', 'application/pdf', 10 * 1024 * 1024 + 1)]);
    await fixture.whenStable();
    expect(added).toHaveLength(0);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('larger than 10 MB');
  });

  it('names a pasted screenshot and lists it with a remove button', async () => {
    const { fixture, dropzone } = await render(true);

    const event = pasteEvent([file('image.png', 'image/png')]);
    document.body.dispatchEvent(event);
    await fixture.whenStable();

    expect(event.defaultPrevented).toBe(true);
    expect(dropzone.files()[0].name).toMatch(/^screenshot-\d{4}-\d{2}-\d{2}-\d{6}\.png$/);

    const remove = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      'button[aria-label^="Remove screenshot-"]',
    )!;
    remove.click();
    await fixture.whenStable();
    expect(dropzone.files()).toHaveLength(0);
  });

  it('ignores pastes outside unless capturePaste is set, and plain text pastes', async () => {
    const { dropzone } = await render(false);
    document.body.dispatchEvent(pasteEvent([file('image.png', 'image/png')]));
    expect(dropzone.files()).toHaveLength(0);

    const text = pasteEvent([]);
    document.body.dispatchEvent(text);
    expect(text.defaultPrevented).toBe(false);
  });

  it('builds sortable screenshot names', () => {
    expect(screenshotName(file('image.jpeg', 'image/jpeg'), new Date(2026, 9, 28, 9, 5, 7))).toBe(
      'screenshot-2026-10-28-090507.jpg',
    );
  });
});
