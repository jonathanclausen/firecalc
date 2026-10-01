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
        path: 'goal',
        loadComponent: () => import('./goal/goal-page').then((m) => m.GoalPage),
      },
    ],
  },
];
