# firecalc backend

ASP.NET Core (.NET 10) minimal API with EF Core and PostgreSQL. It stores the planner data:
accounts, dated balance snapshots and a FIRE goal. The design is in the project notes (`planner-design.md`).

## Sign-in and access

The frontend signs in with Firebase Authentication (Google, Facebook, or email and password) and sends the
Firebase ID token as `Authorization: Bearer <token>`. The API validates it against Firebase's keys for
`Auth:FirebaseProjectId` and requires a verified email. With `Auth:OpenSignUp` anyone with a verified email
gets in; otherwise only `Auth:AllowedEmails` (empty means nobody). Firebase keeps one user per email, so
every login of a person carries the same `sub`; users are created on their first request, keyed by it. A
token with a new `sub` whose verified email matches an existing user takes that user over (how accounts
from before Firebase carried over).

| Setting | Env var on Cloud Run | Notes |
|---|---|---|
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | Npgsql connection string |
| `Auth:FirebaseProjectId` | `Auth__FirebaseProjectId` | Firebase/GCP project id; tokens must be issued for it |
| `Auth:OpenSignUp` | `Auth__OpenSignUp` | `true` lets anyone with a verified email in; default `false` |
| `Auth:AllowedEmails` | `Auth__AllowedEmails__0` | One variable per allowed email, used while sign-up is closed |
| `Auth:AdminEmails` | `Auth__AdminEmails__0` | Accounts that see the admin page (`/planner/admin`); everyone else gets 404 |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins__0` | The frontend's origin |
| `Auth:Authority` | `Auth__Authority` | Token issuer, default Firebase's for the project; override only to test against a local issuer |
| `Database:MigrateOnStartup` | `Database__MigrateOnStartup` | Default `true`; applies EF migrations on boot |

## Endpoints

All under `/api` and require a signed-in user with a verified email (and an allow-listed one while sign-up is closed). `GET /healthz` is public.

| Method | Path | |
|---|---|---|
| GET, DELETE | `/me` | Signed-in user. DELETE removes the user and all their data |
| GET, POST | `/accounts` | `?includeArchived=true` to include archived. POST `{ name, type, partOfHome? }`; `partOfHome` marks a `loan` taken for the home |
| PUT, DELETE | `/accounts/{id}` | Delete only works while the account has no balances; archive otherwise |
| GET, PUT | `/accounts/{id}/balances` | Balances entered by hand for accounts without transactions. PUT `{ date, balance, loan? }` adds or replaces that date's balance. For a `property` account `balance` is the home's value and `loan` what is owed on it; for a `loan` account `balance` is what is owed |
| DELETE | `/accounts/{id}/balances/{date}` | |
| GET, PUT, DELETE | `/goal` | `{ name?, targetAmount, targetDate?, expectedAnnualReturnPct? }`; GET is 204 when unset |
| GET | `/dashboard` | Net worth today (portfolio live, other accounts at their latest balance, a home at its value less the loan, a loan as a negative amount), a series at month ends and balance dates, each account's part, change over the last month, goal progress. `?includeHome=false` leaves homes and loans marked `partOfHome` out of all of it |
| GET | `/portfolio` | Positions, cash and gains per investment account. `?date=` values on another day |
| GET | `/portfolio/values` | Each portfolio account's value on `?date=` |
| GET | `/portfolio/history` | All investment accounts together, day by day for `?from=&to=`: value, net money put in, and time-weighted return in percent |
| GET, POST | `/accounts/{id}/transactions` | Investment accounts only. `{ date, type, instrument?: { id? \| isin? \| symbol?, name? }, quantity?, price?, amount, note? }` |
| PUT, DELETE | `/transactions/{id}` | |
| POST | `/accounts/{id}/import/{nordnet\|saxo}` | Body is the Nordnet CSV or Saxo .xlsx export as-is. Previews unless `?commit=true`; rows already imported are skipped |
| PUT | `/accounts/{id}/holdings` | `{ instrument, quantity, unitPrice?, amount?, date?, averagePrice? }`: sets how many shares you own. The first shares need `unitPrice` (per share, in the share's currency) or `amount`; later changes default to today's price. `date` (default today, not in the future) books the change on that day and converts a foreign `unitPrice` at that day's exchange rate. `averagePrice` corrects the average price paid per share (GAK, in the share's currency) by booking a `costCorrection` that sets the cost without moving money, so it also works for imported or transferred shares. Delete that row to undo it. The change is booked as a buy or sale, paired with money in or out so cash stays put |
| GET | `/instruments/quote` | `?symbol=` latest price, to check a search result against your broker |
| GET | `/instruments/search` | `?q=` name, ticker or ISIN |
| PUT | `/instruments/{id}` | `{ symbol }` to fix which price symbol a share uses |

Account types are `investment`, `savings`, `cash`, `property` (a home) and `loan`.

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
Auth__AdminEmails__0=you@gmail.com
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
