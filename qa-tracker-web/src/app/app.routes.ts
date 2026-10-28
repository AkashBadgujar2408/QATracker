import { Route, Routes } from '@angular/router';
import { authGuard, guestGuard, roleGuard, signedInGuard } from './core/auth/auth.guards';
import { Shell } from './core/layout/shell/shell';
import { PlaceholderData } from './features/placeholder/placeholder-page';
import { Role } from './shared/models/role';

const placeholderPage = () =>
  import('./features/placeholder/placeholder-page').then((m) => m.PlaceholderPage);

/** A not-yet-built page: title, breadcrumb, the session that delivers it and (optionally) who may open it. */
function placeholder(
  path: string,
  heading: string,
  session: string,
  roles?: readonly Role[],
): Route {
  const placeholderData: PlaceholderData = { heading, session };
  return {
    path,
    title: heading,
    data: { breadcrumb: heading, placeholder: placeholderData, roles },
    loadComponent: placeholderPage,
  };
}

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
      placeholder('home', 'Home', 'S22'),
      placeholder('testing', 'Testing', 'S11', ['Tester', 'Admin']),
      placeholder('my-work', 'My work', 'S17', ['Developer', 'Admin']),
      placeholder('issues', 'Issues', 'S15'),
      placeholder('board', 'Board', 'S20'),
      placeholder('retest-queue', 'Retest queue', 'S17', ['Tester', 'Admin']),
      placeholder('sprints', 'Sprints', 'S08 / S29'),
      {
        path: 'dashboards',
        data: { breadcrumb: 'Dashboards' },
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'overview' },
          placeholder('overview', 'Overview', 'S22'),
          placeholder('sprint', 'Sprint dashboard', 'S23'),
          placeholder('developers', 'Developer dashboard', 'S23'),
          placeholder('quality', 'Quality dashboard', 'S24'),
          placeholder('clients', 'Client health', 'S24'),
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
        ],
      },
      {
        // Hidden living style guide (not in the navigation).
        path: 'dev/ui',
        title: 'UI style guide',
        data: { breadcrumb: 'UI style guide' },
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
