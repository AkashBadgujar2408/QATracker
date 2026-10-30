import { Role } from '../../shared/models/role';

export interface NavItem {
  readonly label: string;
  /** Lucide icon name registered in the sidebar. */
  readonly icon: string;
  readonly link: string;
  /** Roles that see the item; undefined = everyone. Routes enforce the same roles (roleGuard). */
  readonly roles?: readonly Role[];
  readonly children?: readonly NavItem[];
}

/** Main navigation (plan 10.3). */
export const NAV_ITEMS: readonly NavItem[] = [
  { label: 'Home', icon: 'lucideHouse', link: '/home' },
  { label: 'Testing', icon: 'lucideFlaskConical', link: '/testing', roles: ['Tester', 'Admin'] },
  { label: 'My work', icon: 'lucideBriefcase', link: '/my-work', roles: ['Developer', 'Admin'] },
  {
    label: 'Deploy notice',
    icon: 'lucideRocket',
    link: '/deploy-notice',
    roles: ['Developer', 'Admin'],
  },
  { label: 'Issues', icon: 'lucideList', link: '/issues' },
  { label: 'Board', icon: 'lucideSquareKanban', link: '/board' },
  {
    label: 'Retest queue',
    icon: 'lucideRotateCcw',
    link: '/retest-queue',
    roles: ['Tester', 'Admin'],
  },
  { label: 'Sprints', icon: 'lucideFlag', link: '/sprints' },
  {
    label: 'Dashboards',
    icon: 'lucideChartPie',
    link: '/dashboards',
    children: [
      { label: 'Overview', icon: 'lucideChartPie', link: '/dashboards/overview' },
      { label: 'Sprint', icon: 'lucideChartPie', link: '/dashboards/sprint' },
      { label: 'Developers', icon: 'lucideChartPie', link: '/dashboards/developers' },
      { label: 'Quality', icon: 'lucideChartPie', link: '/dashboards/quality' },
      { label: 'Client health', icon: 'lucideChartPie', link: '/dashboards/clients' },
    ],
  },
  { label: 'Masters', icon: 'lucideFolderTree', link: '/masters', roles: ['Tester', 'Admin'] },
  {
    label: 'Import',
    icon: 'lucideFileUp',
    link: '/import',
    roles: ['Tester', 'Admin'],
  },
  { label: 'Users', icon: 'lucideUsers', link: '/admin/users', roles: ['Admin'] },
  { label: 'E-mail', icon: 'lucideMail', link: '/admin/email', roles: ['Admin'] },
  {
    label: 'Settings',
    icon: 'lucideSettings',
    link: '/admin/settings',
    roles: ['Admin'],
  },
];

/** Navigation for a role: the items (and dashboard children) that role may see. */
export function navItemsFor(role: Role | null, items: readonly NavItem[] = NAV_ITEMS): NavItem[] {
  return items
    .filter((item) => !item.roles || (role !== null && item.roles.includes(role)))
    .map((item) =>
      item.children ? { ...item, children: navItemsFor(role, item.children) } : item,
    );
}
