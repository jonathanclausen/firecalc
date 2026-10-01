# FireCalc

A personal FIRE (financial independence, retire early) planner. The first iteration is a compound interest calculator with recurring deposits; scenarios and personal finance tracking come next.

## Repository layout

```
frontend/   Angular app (standalone components, signals, SSR)
backend/    .NET API, added when scenarios and tracking need persistence
```

## Frontend

Requires Node 24 (see `.nvmrc`).

```bash
cd frontend
npm install
npm start          # http://localhost:4200
npm test           # unit tests (Vitest)
npm run build
npm run serve:ssr  # serve the production build with server-side rendering on :4000
```

### Server-side rendering

The app uses Angular SSR (`@angular/ssr` with an Express server in `src/server.ts`). Every route is rendered on the server and hydrated in the browser. Saved inputs and currency live in localStorage, so the server renders the defaults and the browser restores the visitor's values right after hydration.

`npm run build` produces `dist/frontend/browser` (static assets) and `dist/frontend/server/server.mjs` (the Node server, listening on `PORT`, default 4000). Angular only serves requests whose `Host` header is allow-listed: `localhost` is in `angular.json`, and a deployed host such as a Cloud Run domain is added with the `NG_ALLOWED_HOSTS` environment variable (comma-separated).

### Where things live

- `src/app/core/finance/compound-interest.ts` is the calculation engine. It is plain TypeScript with no Angular dependencies so it can be tested in isolation and later mirrored by the backend.
- `src/app/core/settings/currency.ts` holds the currency list. DKK is the default; add an entry to `CURRENCIES` or change `DEFAULT_CURRENCY` to switch.
- `src/app/features/compound-interest/` is the calculator page, its chart and year-by-year table.
- Design tokens (colors, radii, light and dark themes) are CSS custom properties in `src/styles.scss`.

Inputs and the chosen currency are remembered in the browser's localStorage.

### How the math works

The engine simulates month by month. The nominal annual return is converted to an effective monthly rate that matches the chosen compounding frequency, deposits land at the start or end of each deposit period, and the deposit can grow by a fixed percentage each year. The "today's money" figures discount the balance by the expected inflation rate.
