import { expect, test } from '@playwright/test';
import { pasteScreenshot, signIn, world } from './world';

/**
 * The three critical flows of plan 15.1, in order on one issue:
 * 1. the tester logs an issue with a pasted screenshot and marks testing completed,
 * 2. the developer picks it (assigned + in progress) and marks it fixed on UAT,
 * 3. the tester retests it from the retest queue and passes it (Verified).
 */
test.describe.configure({ mode: 'serial' });

test('tester logs an issue with a pasted screenshot', async ({ page }) => {
  const w = world();
  await signIn(page, w.users.tester);
  await page.goto(`/testing?client=${w.client.id}&project=${w.project.id}&sprint=${w.sprint.id}`);

  const form = page.getByRole('region', { name: 'Issue form' });
  const modulePicker = form.getByRole('combobox', { name: 'Module' });
  await modulePicker.fill(w.module.name);
  await page.getByRole('option', { name: w.module.name, exact: true }).click();

  await form.getByLabel('Title').fill('Invoice total shows 0.00 after adding a line');
  await form.getByRole('textbox', { name: 'Actual result' }).fill('The total stays at 0.00.');
  await form.getByRole('radio', { name: 'High' }).first().click();
  await form
    .getByRole('radiogroup', { name: /Priority/ })
    .getByRole('radio', { name: 'High' })
    .click();

  await pasteScreenshot(page);
  await expect(form.getByText(/screenshot-.*\.png/)).toBeVisible();

  // Pending files upload right after the issue is created.
  const uploaded = page.waitForResponse(
    (r) => r.request().method() === 'POST' && /\/api\/issues\/[^/]+\/attachments$/.test(r.url()),
  );
  await form.getByRole('button', { name: 'Save', exact: true }).click();
  expect((await uploaded).status()).toBe(201);

  const list = page.getByRole('region', { name: 'Issues of this sprint' });
  await expect(list.getByText(`${w.project.key}-1`)).toBeVisible();

  // The screenshot reached the issue.
  await page.goto(`/issues/${w.project.key}-1`);
  await expect(page.getByRole('img', { name: /screenshot-.*\.png/ }).first()).toBeVisible();

  // Hand over to the developers.
  await page.goto(`/testing?client=${w.client.id}&project=${w.project.id}&sprint=${w.sprint.id}`);
  await page.getByRole('button', { name: 'Mark testing completed' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Mark testing completed' }).click();
  await expect(dialog).toBeHidden();
});

test('developer picks the issue and marks it fixed on UAT', async ({ page }) => {
  const w = world();
  await signIn(page, w.users.developer);
  await page.goto(`/issues/${w.project.key}-1`);

  await page.getByRole('button', { name: 'Pick', exact: true }).click();
  // Picking assigns the issue to the developer and starts work in one step.
  await expect(page.getByText('In Progress', { exact: true }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Mark fixed' }).click();

  const dialog = page.getByRole('dialog');
  await dialog.getByRole('checkbox', { name: new RegExp(w.uat.name) }).check();
  await dialog.getByLabel('What was fixed?').fill('Total is recalculated when a line is added.');
  await dialog.getByRole('button', { name: 'Mark fixed' }).click();
  await expect(dialog).toBeHidden();

  await expect(page.getByText('Fixed', { exact: true }).first()).toBeVisible();
});

test('tester retests the fix from the retest queue', async ({ page }) => {
  const w = world();
  await signIn(page, w.users.tester);
  await page.goto('/retest-queue');

  const row = page.getByRole('listitem').filter({ hasText: `${w.project.key}-1` });
  await expect(row).toBeVisible();
  await row.getByRole('button', { name: 'Pass' }).click();
  await expect(row).toBeHidden();

  // Real keystrokes (not fill): "/" focuses the search, the key opens the issue (S31 – the shell must not swallow keys).
  await page.keyboard.press('/');
  await page.keyboard.type(`${w.project.key.toLowerCase()}-1`);
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(new RegExp(`/issues/${w.project.key}-1$`));
  await expect(page.getByText('Verified', { exact: true }).first()).toBeVisible();
});
