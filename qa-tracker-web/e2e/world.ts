import { readFileSync } from 'node:fs';
import { expect, Page } from '@playwright/test';

export const API_URL = 'http://localhost:5129';
export const worldFile = 'e2e/.state/world.json';

export interface E2eUser {
  id: number;
  userName: string;
  fullName: string;
  password: string;
}

/** What seed.setup.ts created (e2e/.state/world.json – git-ignored, rebuilt every run). */
export interface World {
  users: { tester: E2eUser; developer: E2eUser };
  client: { id: number; name: string };
  project: { id: number; name: string; key: string };
  module: { id: number; name: string };
  uat: { id: number; name: string };
  sprint: { id: number; name: string };
}

export const world = (): World => JSON.parse(readFileSync(worldFile, 'utf8')) as World;

/** Signs in through the login page (fresh browser context per test). */
export async function signIn(page: Page, user: E2eUser): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('User name').fill(user.userName);
  await page.getByLabel('Password', { exact: true }).fill(user.password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).not.toHaveURL(/\/login/);
}

/** A small PNG (a "screenshot") made in the page, so no binary fixture is needed. */
export async function pasteScreenshot(page: Page): Promise<void> {
  await page.evaluate(async () => {
    const canvas = document.createElement('canvas');
    canvas.width = 320;
    canvas.height = 200;
    const context = canvas.getContext('2d')!;
    context.fillStyle = '#e45858';
    context.fillRect(0, 0, 320, 200);
    context.fillStyle = '#ffffff';
    context.fillText('Invoice total 0.00', 20, 100);
    const blob = await new Promise<Blob>((resolve) =>
      canvas.toBlob((b) => resolve(b!), 'image/png'),
    );
    const data = new DataTransfer();
    data.items.add(new File([blob], 'image.png', { type: 'image/png' }));
    document.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true }));
  });
}
