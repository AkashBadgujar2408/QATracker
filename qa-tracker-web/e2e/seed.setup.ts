import { mkdirSync, writeFileSync } from 'node:fs';
import { APIRequestContext, expect, request, test as setup } from '@playwright/test';
import { API_URL, World, worldFile } from './world';

/**
 * Builds the E2E world through the API on the fresh QaTracker_E2E database: signs the seeded admin in (forced
 * password change), creates a tester and a developer (each changes the temporary password), a client / project /
 * module / UAT environment and a Testing sprint. Passwords are random per run.
 */
const password = (role: string) => `E2e-${role}-${Math.random().toString(36).slice(2, 10)}9`;

async function signIn(api: APIRequestContext, userName: string, pwd: string): Promise<string> {
  const login = await api.post('/api/auth/login', { data: { userName, password: pwd } });
  expect(login.status(), await login.text()).toBe(200);
  return (await login.json()).accessToken as string;
}

async function changePassword(
  api: APIRequestContext,
  token: string,
  current: string,
  next: string,
): Promise<string> {
  const change = await api.post('/api/auth/change-password', {
    headers: { Authorization: `Bearer ${token}` },
    data: { currentPassword: current, newPassword: next },
  });
  expect(change.status(), await change.text()).toBe(200);
  return (await change.json()).accessToken as string;
}

setup('seed users, masters and a testing sprint', async () => {
  const api = await request.newContext({ baseURL: API_URL });
  const adminPassword = password('admin');
  const adminToken = await changePassword(
    api,
    await signIn(api, 'admin', process.env['E2E_ADMIN_PASSWORD']!),
    process.env['E2E_ADMIN_PASSWORD']!,
    adminPassword,
  );
  const asAdmin = { Authorization: `Bearer ${adminToken}` };

  const users: World['users'] = {} as World['users'];
  for (const [role, userName, fullName] of [
    ['Tester', 'emily.tester', 'Emily Clarke'],
    ['Developer', 'john.dev', 'John Miller'],
  ] as const) {
    const created = await api.post('/api/users', {
      headers: asAdmin,
      data: { userName, fullName, email: `${userName}@e2e.local`, role },
    });
    expect(created.status(), await created.text()).toBe(201);
    const { user, temporaryPassword } = await created.json();
    const own = password(role);
    await changePassword(
      api,
      await signIn(api, userName, temporaryPassword),
      temporaryPassword,
      own,
    );
    users[role === 'Tester' ? 'tester' : 'developer'] = {
      id: user.id,
      userName,
      fullName,
      password: own,
    };
  }

  const tester = {
    Authorization: `Bearer ${await signIn(api, users.tester.userName, users.tester.password)}`,
  };
  const post = async (url: string, data: object) => {
    const response = await api.post(url, { headers: tester, data });
    expect(response.status(), `${url}: ${await response.text()}`).toBe(201);
    return response.json();
  };

  const client = await post('/api/clients', { name: 'Northwind Traders' });
  const project = await post(`/api/clients/${client.id}/projects`, {
    name: 'Order Portal',
    key: 'ORD',
  });
  const module = await post(`/api/projects/${project.id}/modules`, { name: 'Invoices' });
  const uat = await post(`/api/projects/${project.id}/environments`, {
    name: 'UAT',
    type: 'UAT',
    url: 'https://uat.orders.example.com',
  });
  const membersSet = await api.put(`/api/projects/${project.id}/members`, {
    headers: tester,
    data: { userIds: [users.tester.id, users.developer.id] },
  });
  expect(membersSet.ok(), await membersSet.text()).toBeTruthy();
  const phases = await (await api.get('/api/lookups/phases', { headers: tester })).json();
  const sprint = await post(`/api/projects/${project.id}/sprints`, {
    name: 'Sprint 1',
    phaseId: phases[0].id,
  });

  const world: World = {
    users,
    client: { id: client.id, name: client.name },
    project: { id: project.id, name: project.name, key: project.key },
    module: { id: module.id, name: module.name },
    uat: { id: uat.id, name: uat.name },
    sprint: { id: sprint.id, name: sprint.name },
  };
  mkdirSync('e2e/.state', { recursive: true });
  writeFileSync(worldFile, JSON.stringify(world, null, 2));
  await api.dispose();
});
