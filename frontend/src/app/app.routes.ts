import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./features/landing/landing-page').then((m) => m.LandingPage),
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
