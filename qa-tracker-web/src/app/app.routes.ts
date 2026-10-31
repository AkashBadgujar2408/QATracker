import { Route, Routes } from '@angular/router';
import { authGuard, guestGuard, roleGuard, signedInGuard } from './core/auth/auth.guards';
import { Shell } from './core/layout/shell/shell';
import { Role } from './shared/models/role';
import { provideCharts } from './shared/charts/echarts';

/** A dashboard page. ECharts is provided only on chart routes (its own lazy chunk). */ function chartRoute(
  path: string,
  heading: string,
  load: Route['loadComponent'],
): Route {
  return {
    path,
    title: heading,
    data: { breadcrumb: heading },
    providers: [provideCharts()],
    loadComponent: load,
  };
}
/** The overview dashboard (plan 9.1) – also the home page. */ const overviewPage = () =>
  import('./features/dashboards/overview-page').then((m) => m.OverviewPage);

export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'change-password',
    title: 'Change password',
    canActivate: [signedInGuard],
    loadComponent: () =>
      import('./features/auth/change-password-page').then((m) => m.ChangePasswordPage),
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    canActivateChild: [authGuard, roleGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'home' },
      chartRoute('home', 'Home', overviewPage),
      {
        path: 'testing',
        title: 'Testing',
        data: { breadcrumb: 'Testing', roles: ['Tester', 'Admin'] satisfies Role[] },
        loadComponent: () => import('./features/testing/testing-page').then((m) => m.TestingPage),
      },
      {
        path: 'my-work',
        title: 'My work',
        data: { breadcrumb: 'My work', roles: ['Developer', 'Admin'] satisfies Role[] },
        loadComponent: () => import('./features/work/my-work-page').then((m) => m.MyWorkPage),
      },
      {
        path: 'settings/notifications',
        title: 'Notification settings',
        data: { breadcrumb: 'Notification settings' },
        loadComponent: () =>
          import('./features/settings/notification-settings-page').then(
            (m) => m.NotificationSettingsPage,
          ),
      },
      {
        path: 'notifications',
        title: 'Notifications',
        data: { breadcrumb: 'Notifications' },
        loadComponent: () =>
          import('./features/notifications/notifications-page').then((m) => m.NotificationsPage),
      },
      {
        path: 'deploy-notice',
        title: 'Deploy notice',
        data: { breadcrumb: 'Deploy notice', roles: ['Developer', 'Admin'] satisfies Role[] },
        loadComponent: () =>
          import('./features/work/deploy-notice-page').then((m) => m.DeployNoticePage),
      },
      {
        path: 'issues',
        data: { breadcrumb: 'Issues' },
        children: [
          {
            path: '',
            title: 'Issues',
            loadComponent: () =>
              import('./features/issues/issue-list-page').then((m) => m.IssueListPage),
          },
          {
            path: ':key',
            title: 'Issue',
            loadComponent: () =>
              import('./features/issues/issue-detail-page').then((m) => m.IssueDetailPage),
          },
        ],
      },
      {
        path: 'board',
        title: 'Board',
        data: { breadcrumb: 'Board' },
        loadComponent: () => import('./features/board/board-page').then((m) => m.BoardPage),
      },
      {
        path: 'retest-queue',
        title: 'Retest queue',
        data: { breadcrumb: 'Retest queue', roles: ['Tester', 'Admin'] satisfies Role[] },
        loadComponent: () =>
          import('./features/work/retest-queue-page').then((m) => m.RetestQueuePage),
      },
      {
        path: 'import',
        title: 'Import from Excel',
        data: { breadcrumb: 'Import from Excel', roles: ['Tester', 'Admin'] satisfies Role[] },
        loadComponent: () => import('./features/import/import-page').then((m) => m.ImportPage),
      },
      {
        path: 'sprints',
        title: 'Sprints',
        data: { breadcrumb: 'Sprints' },
        loadComponent: () => import('./features/sprints/sprints-page').then((m) => m.SprintsPage),
      },
      {
        path: 'dashboards',
        data: { breadcrumb: 'Dashboards' },
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'overview' },
          chartRoute('overview', 'Overview', overviewPage),
          chartRoute('sprint', 'Sprint dashboard', () =>
            import('./features/dashboards/sprint-dashboard-page').then(
              (m) => m.SprintDashboardPage,
            ),
          ),
          chartRoute('developers', 'Developer dashboard', () =>
            import('./features/dashboards/developers-page').then((m) => m.DevelopersPage),
          ),
          chartRoute('quality', 'Quality dashboard', () =>
            import('./features/dashboards/quality-page').then((m) => m.QualityPage),
          ),
          chartRoute('clients', 'Client health', () =>
            import('./features/dashboards/clients-page').then((m) => m.ClientsPage),
          ),
        ],
      },
      {
        path: 'masters',
        title: 'Masters',
        data: { breadcrumb: 'Masters', roles: ['Tester', 'Admin'] satisfies Role[] },
        loadComponent: () => import('./features/masters/masters-page').then((m) => m.MastersPage),
      },
      {
        path: 'admin',
        data: { breadcrumb: 'Admin', roles: ['Admin'] satisfies Role[] },
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'users' },
          {
            path: 'users',
            title: 'Users',
            data: { breadcrumb: 'Users' },
            loadComponent: () =>
              import('./features/admin/users/users-page').then((m) => m.UsersPage),
          },
          {
            path: 'email',
            title: 'E-mail',
            data: { breadcrumb: 'E-mail' },
            loadComponent: () =>
              import('./features/admin/email/email-admin-page').then((m) => m.EmailAdminPage),
          },
          {
            path: 'settings',
            title: 'Settings',
            data: { breadcrumb: 'Settings' },
            loadComponent: () =>
              import('./features/admin/settings/settings-page').then((m) => m.SettingsPage),
          },
        ],
      },
      {
        // Hidden living style guide (not in the navigation).
        path: 'dev/ui',
        title: 'UI style guide',
        data: { breadcrumb: 'UI style guide' },
        providers: [provideCharts()],
        loadComponent: () => import('./features/dev/ui-showcase').then((m) => m.UiShowcase),
      },
      {
        path: '**',
        title: 'Not found',
        loadComponent: () =>
          import('./features/placeholder/not-found-page').then((m) => m.NotFoundPage),
      },
    ],
  },
];
