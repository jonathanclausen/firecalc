import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  // Personal pages depend on the sign-in kept in the browser, so they render there.
  {
    path: 'planner',
    renderMode: RenderMode.Client,
  },
  {
    path: 'planner/**',
    renderMode: RenderMode.Client,
  },
  {
    path: '**',
    renderMode: RenderMode.Server,
  },
];
