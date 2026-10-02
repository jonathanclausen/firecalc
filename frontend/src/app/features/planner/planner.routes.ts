import { Routes } from '@angular/router';
import { PlannerShell } from './planner-shell';

export const plannerRoutes: Routes = [
  {
    path: '',
    component: PlannerShell,
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('./dashboard/dashboard-page').then((m) => m.DashboardPage),
      },
      {
        path: 'snapshot',
        loadComponent: () => import('./snapshot/snapshot-page').then((m) => m.SnapshotPage),
      },
      {
        path: 'snapshot/:id',
        loadComponent: () => import('./snapshot/snapshot-page').then((m) => m.SnapshotPage),
      },
      {
        path: 'accounts',
        loadComponent: () => import('./accounts/accounts-page').then((m) => m.AccountsPage),
      },
      {
        path: 'portfolio',
        loadComponent: () => import('./portfolio/portfolio-page').then((m) => m.PortfolioPage),
      },
      {
        path: 'portfolio/import',
        loadComponent: () => import('./portfolio/import-page').then((m) => m.ImportPage),
      },
      {
        path: 'portfolio/:accountId',
        loadComponent: () =>
          import('./portfolio/transactions-page').then((m) => m.TransactionsPage),
      },
      {
        path: 'goal',
        loadComponent: () => import('./goal/goal-page').then((m) => m.GoalPage),
      },
    ],
  },
];
