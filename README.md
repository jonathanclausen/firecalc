# FireCalc

A personal FIRE (financial independence, retire early) planner. It has a public compound interest calculator and a signed-in "My finances" area where you track your accounts with dated snapshots and follow your progress toward a FIRE number. Scenarios come next.

## Repository layout

```
frontend/   Angular app (standalone components, signals, SSR)
backend/    .NET API with Postgres (accounts, snapshots, goal), see backend/README.md
```

Production runs on Google Cloud Run with a Neon Postgres database, deployed from `main` by GitHub Actions. Setup: [deploy/README.md](deploy/README.md).

## Run everything

```bash
docker compose up --build    # http://localhost:4000 (API on http://localhost:5080)
```

Sign-in uses Google. Only allow-listed Google accounts get in: put yours in `backend/.env.local` (git-ignored) as `Auth__AllowedEmails__0=you@gmail.com`.

## Frontend

Requires Node 24 (see `.nvmrc`).

```bash
cd frontend
npm install
GOOGLE_CLIENT_ID=<client id> npm start   # http://localhost:4200, API expected on :5080
npm test           # unit tests (Vitest)
npm run build
npm run serve:ssr  # serve the production build with server-side rendering on :4000
```

### Server-side rendering

The app uses Angular SSR (`@angular/ssr` with an Express server in `src/server.ts`). Every route is rendered on the server and hydrated in the browser. Saved inputs and currency live in localStorage, so the server renders the defaults and the browser restores the visitor's values right after hydration.

`npm run build` produces `dist/frontend/browser` (static assets) and `dist/frontend/server/server.mjs` (the Node server, listening on `PORT`, default 4000). Angular only serves requests whose `Host` header is allow-listed: `localhost` is in `angular.json`, and a deployed host such as a Cloud Run domain is added with the `NG_ALLOWED_HOSTS` environment variable (comma-separated).

### My finances

The `/planner` pages render in the browser only, because they depend on the Google sign-in kept there. The browser signs in with Google Identity Services and sends the Google ID token to `/api`. The Node server forwards `/api` to the .NET API (`API_URL`, default `http://localhost:5080`), so the browser needs no CORS and the API address stays a server setting. It also serves `/app-config.json` with the Google client id from `GOOGLE_CLIENT_ID`. Balances are shown in the user's own currency (DKK), whatever the header's currency selector says, because the planner doesn't convert currencies.

### Where things live

- `src/app/core/finance/compound-interest.ts` is the calculation engine. It is plain TypeScript with no Angular dependencies so it can be tested in isolation and later mirrored by the backend.
- `src/app/core/settings/currency.ts` holds the currency list. DKK is the default; add an entry to `CURRENCIES` or change `DEFAULT_CURRENCY` to switch.
- `src/app/features/compound-interest/` is the calculator page, its chart and year-by-year table.
- `src/app/features/planner/` is "My finances": overview with net worth chart and goal, snapshot form, accounts and goal pages. Sign-in lives in `src/app/core/auth/`, API types in `src/app/core/api/`, goal maths in `src/app/core/finance/goal.ts`.
- `src/app/core/i18n/translations.ts` holds every UI string. Danish is the default and English can be picked in the header. The choice is stored in a `firecalc.lang` cookie so the server renders the right language.
- Design tokens (colors, radii, light and dark themes) are CSS custom properties in `src/styles.scss`.

Inputs and the chosen currency are remembered in the browser's localStorage.

### How the math works

The engine simulates month by month. The nominal annual return is converted to an effective monthly rate that matches the chosen compounding frequency, deposits land at the start or end of each deposit period, and the deposit can grow by a fixed percentage each year. The "today's money" figures discount the balance by the expected inflation rate.
