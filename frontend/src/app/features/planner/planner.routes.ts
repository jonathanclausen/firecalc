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
      // Snapshots became balances on each account.
      { path: 'snapshot', redirectTo: 'accounts' },
      {
        path: 'accounts',
        loadComponent: () => import('./accounts/accounts-page').then((m) => m.AccountsPage),
      },
      {
        path: 'home',
        loadComponent: () => import('./home/home-page').then((m) => m.HomePage),
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
        path: 'future',
        loadComponent: () => import('./future/future-page').then((m) => m.FuturePage),
      },
      {
        path: 'fire',
        loadComponent: () => import('./fire/fire-page').then((m) => m.FirePage),
      },
      {
        path: 'fire/:id',
        loadComponent: () => import('./fire/scenario-page').then((m) => m.ScenarioPage),
      },
      // Scenarios moved from Fremtid to their own FIRE page.
      { path: 'future/:id', redirectTo: 'fire/:id' },
      {
        path: 'welcome',
        loadComponent: () => import('./onboarding/welcome-page').then((m) => m.WelcomePage),
      },
      {
        path: 'admin',
        loadComponent: () => import('./admin/admin-page').then((m) => m.AdminPage),
      },
      {
        path: 'goal',
        loadComponent: () => import('./goal/goal-page').then((m) => m.GoalPage),
      },
      {
        path: 'account',
        loadComponent: () => import('./account/account-page').then((m) => m.AccountPage),
      },
    ],
  },
];
