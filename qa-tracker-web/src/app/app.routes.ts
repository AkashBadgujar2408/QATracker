import { Routes } from '@angular/router';
import { Shell } from './core/layout/shell/shell';
import { PlaceholderData } from './features/placeholder/placeholder-page';

const placeholderPage = () =>
  import('./features/placeholder/placeholder-page').then((m) => m.PlaceholderPage);

/** A not-yet-built page: title, breadcrumb and the session that delivers it. */
function placeholder(path: string, heading: string, session: string) {
  const placeholderData: PlaceholderData = { heading, session };
  return {
    path,
    title: heading,
    data: { breadcrumb: heading, placeholder: placeholderData },
    loadComponent: placeholderPage,
  };
}

export const routes: Routes = [
  {
    path: '',
    component: Shell,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'home' },
      placeholder('home', 'Home', 'S22'),
      placeholder('testing', 'Testing', 'S11'),
      placeholder('my-work', 'My work', 'S17'),
      placeholder('issues', 'Issues', 'S15'),
      placeholder('board', 'Board', 'S20'),
      placeholder('retest-queue', 'Retest queue', 'S17'),
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
      placeholder('masters', 'Masters', 'S07'),
      placeholder('admin', 'Admin', 'S05 / S28'),
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
