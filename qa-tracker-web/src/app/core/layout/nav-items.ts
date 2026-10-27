import { Role } from '../../shared/models/role';

export interface NavItem {
  readonly label: string;
  /** Lucide icon name registered in the sidebar. */
  readonly icon: string;
  readonly link: string;
  /** Roles that see the item; undefined = everyone. Enforced once auth exists (S05). */
  readonly roles?: readonly Role[];
  readonly children?: readonly NavItem[];
}

/** Main navigation (plan 10.3). */
export const NAV_ITEMS: readonly NavItem[] = [
  { label: 'Home', icon: 'lucideHouse', link: '/home' },
  { label: 'Testing', icon: 'lucideFlaskConical', link: '/testing', roles: ['Tester', 'Admin'] },
  { label: 'My work', icon: 'lucideBriefcase', link: '/my-work', roles: ['Developer', 'Admin'] },
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
  { label: 'Admin', icon: 'lucideSettings', link: '/admin', roles: ['Admin'] },
];
