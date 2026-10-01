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
  { path: '**', redirectTo: 'compound-interest' },
];
