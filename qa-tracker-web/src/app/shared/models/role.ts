export const ROLES = ['Tester', 'Developer', 'Admin'] as const;
export type Role = (typeof ROLES)[number];
