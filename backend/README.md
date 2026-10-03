# firecalc backend

ASP.NET Core (.NET 10) minimal API with EF Core and PostgreSQL. It stores the planner data:
accounts, dated balance snapshots and a FIRE goal. The design is in the project notes (`planner-design.md`).

## Sign-in and access

The frontend signs in with Google Identity Services and sends the Google ID token as
`Authorization: Bearer <token>`. The API validates it against Google's keys and the configured client id,
then only lets in accounts listed in `Auth:AllowedEmails` with a verified email. With an empty list nobody
gets in. Users are created on their first request, keyed by Google's `sub`.

| Setting | Env var on Cloud Run | Notes |
|---|---|---|
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | Npgsql connection string |
| `Auth:GoogleClientId` | `Auth__GoogleClientId` | OAuth client id from the GCP project |
| `Auth:AllowedEmails` | `Auth__AllowedEmails__0` | One variable per allowed Google account |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins__0` | The frontend's origin |
| `Auth:Authority` | `Auth__Authority` | Token issuer, default Google; override only to test against a local issuer |
| `Database:MigrateOnStartup` | `Database__MigrateOnStartup` | Default `true`; applies EF migrations on boot |

## Endpoints

All under `/api` and require an allowed Google account. `GET /healthz` is public.

| Method | Path | |
|---|---|---|
| GET | `/me` | Signed-in user |
| GET, POST | `/accounts` | `?includeArchived=true` to include archived |
| PUT, DELETE | `/accounts/{id}` | Delete only works while the account has no balances; archive otherwise |
| GET, POST | `/snapshots` | One snapshot per date: `{ date, note?, entries: [{ accountId, balance }] }` |
| GET, PUT, DELETE | `/snapshots/{id}` | |
| GET, PUT, DELETE | `/goal` | `{ name?, targetAmount, targetDate?, expectedAnnualReturnPct? }`; GET is 204 when unset |
| GET | `/dashboard` | Net worth series by account type, latest total, change, goal progress |
| GET | `/portfolio` | Positions, cash and gains per investment account. `?date=` values on another day |
| GET | `/portfolio/values` | Each portfolio account's value on `?date=`, used to pre-fill snapshots |
| GET | `/portfolio/history` | All investment accounts together, day by day for `?from=&to=`: value, net money put in, and time-weighted return in percent |
| GET, POST | `/accounts/{id}/transactions` | Investment accounts only. `{ date, type, instrument?: { id? \| isin? \| symbol?, name? }, quantity?, price?, amount, note? }` |
| PUT, DELETE | `/transactions/{id}` | |
| POST | `/accounts/{id}/import/{nordnet\|saxo}` | Body is the Nordnet CSV or Saxo .xlsx export as-is. Previews unless `?commit=true`; rows already imported are skipped |
| PUT | `/accounts/{id}/holdings` | `{ instrument, quantity, unitPrice?, amount?, date?, averagePrice? }`: sets how many shares you own. The first shares need `unitPrice` (per share, in the share's currency) or `amount`; later changes default to today's price. `date` (default today, not in the future) books the change on that day and converts a foreign `unitPrice` at that day's exchange rate. `averagePrice` corrects the average price paid per share (GAK, in the share's currency) by booking a `costCorrection` that sets the cost without moving money, so it also works for imported or transferred shares. Delete that row to undo it. The change is booked as a buy or sale, paired with money in or out so cash stays put |
| GET | `/instruments/quote` | `?symbol=` latest price, to check a search result against your broker |
| GET | `/instruments/search` | `?q=` name, ticker or ISIN |
| PUT | `/instruments/{id}` | `{ symbol }` to fix which price symbol a share uses |

Account types are `investment`, `savings` and `cash`.

### Portfolio prices

A transaction's `amount` is the signed cash effect in the user's currency (a buy is negative, including
fees), so cash is the sum of amounts and cost uses the average cost method. Prices come from Yahoo
Finance's free, unofficial chart endpoint (symbols found by ISIN, Copenhagen preferred for Danish ISINs) and
currency rates from Frankfurter (ECB). Both are fetched when a portfolio is viewed and the stored ones are
more than six hours old, and daily closes are kept in Postgres, so no scheduled job or API key is needed.
A position without a recent price is valued at what it cost until its symbol is fixed.

## Run locally

Whole app (Postgres, API and frontend) with Docker, from the repo root:

```sh
docker compose up --build    # frontend http://localhost:4000, API http://localhost:5080
```

The Google client id is set in `docker-compose.yml`. To get in, put your Google account in `backend/.env.local` (git-ignored):

```
Auth__AllowedEmails__0=you@gmail.com
```

For quick backend iteration, run only the database in Docker and the API with hot reload:

```sh
docker compose up -d db
dotnet watch --project src/FireCalc.Api    # Development settings point at localhost:5432
```

Here, set `Auth:AllowedEmails` with `dotnet user-secrets` instead (the client id is in `appsettings.Development.json`).

## Tests

Integration tests run the API against a throwaway database on a real Postgres and mint their own ID tokens.

```sh
FIRECALC_TEST_POSTGRES="Host=localhost;Port=5432;Username=postgres;Password=postgres" dotnet test
```

## Migrations

```sh
dotnet tool install --global dotnet-ef
dotnet ef migrations add <Name> -p src/FireCalc.Api -o Data/Migrations
```
