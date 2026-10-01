import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'compound-interest',
  },
  {
    path: 'compound-interest',
    loadComponent: () =>
      import('./features/compound-interest/compound-interest-page').then(
        (m) => m.CompoundInterestPage,
      ),
  },
  {
    path: 'planner',
    loadChildren: () => import('./features/planner/planner.routes').then((m) => m.plannerRoutes),
  },
  { path: '**', redirectTo: 'compound-interest' },
];
