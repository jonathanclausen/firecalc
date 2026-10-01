import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'compound-interest',
  },
  {
    path: 'compound-interest',
    title: 'Compound interest · FireCalc',
    loadComponent: () =>
      import('./features/compound-interest/compound-interest-page').then(
        (m) => m.CompoundInterestPage,
      ),
  },
  { path: '**', redirectTo: 'compound-interest' },
];
