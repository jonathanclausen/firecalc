# Deploying to Google Cloud

FireCalc runs as two Cloud Run services in `europe-west1` (Belgium, close to Neon's Frankfurt region), with the database on Neon's free Postgres tier. GitHub Actions builds and deploys them (`.github/workflows/deploy.yml`):

| | Production | Preview 1 | Preview *n* (2, 3, …) |
|---|---|---|---|
| Deploys | every push to `main` | an open pull request that holds this slot | an open pull request that holds this slot |
| Services | `firecalc-web`, `firecalc-api` | `firecalc-web-preview`, `firecalc-api-preview` | `firecalc-web-preview-n`, `firecalc-api-preview-n` |
| Database | Neon `main` branch (secret `firecalc-db`) | Neon `dev` branch (secret `firecalc-db-preview`) | Neon `preview-n` branch (secret `firecalc-db-preview-n`) |

### Previews

Every pull request from this repository that changes `frontend/` or `backend/` gets a preview environment of its own, deployed when it opens and again on every push. The address is posted as a comment on the pull request (and shown under *Deployments*).

There is a small fixed pool of them (`PREVIEW_SLOTS`, 3 by default) rather than one per pull request, because Google sign-in only works from addresses registered on the OAuth client in advance, with no wildcards. A pull request holds its slot through a `preview-slot-<n>` label until it is merged or closed; then the slot is free for the next one.

- When all slots are taken, the pull request gets a comment saying which ones hold them. Push again once one closes, or add the `preview` label to take over the slot of the pull request that was updated least recently.
- When a slot changes hands and `NEON_API_KEY` is set, its Neon branch is reset from its parent first, so the new pull request starts from a copy of production's data without leftover migrations from the previous one.
- Idle preview services scale to zero and cost nothing, so a free slot just keeps its last deploy until the next pull request takes it.

```
browser ──▶ firecalc-web (public, Angular SSR) ──/api──▶ firecalc-api (private, .NET) ──▶ Neon Postgres
```

- **firecalc-web** is the only public service. It renders pages and forwards `/api` to the API, adding a Google-signed token for its own identity in `X-Serverless-Authorization`, so the user's Google token still travels in `Authorization`.
- **firecalc-api** refuses any caller that isn't the web service, then checks the user's Google token and the email allow-list as usual. It applies database migrations on start-up, so a branch with a new migration changes only its preview database until it reaches `main`.
- Both scale to zero when idle, so the first request after a quiet spell takes a few seconds while Cloud Run and Neon wake up.
- GitHub signs in to Google Cloud with Workload Identity Federation, so no service account key exists anywhere. Only workflows running on `main` can use the production deploy account; branches get a separate account that can only change the preview services, and the preview APIs can only read the preview database secrets.

## Cost

Expected to stay at 0 kr for personal use: Cloud Run's free tier (2 million requests and 180,000 vCPU-seconds a month) covers production and the previews, Neon's free plan includes branches (each preview branch's compute sleeps after 5 idle minutes), and the image registry keeps only the 2 newest images per service and environment (a few cents a month at most if it passes the 0.5 GB free storage). Set a budget alert anyway (step 5).

## One-time setup

1. **Neon.** Create a project at [neon.tech](https://neon.tech) in **AWS Europe Central 1 (Frankfurt)**, then under *Branches* add a branch from `main` for each preview slot: `dev` for slot 1, `preview-2` and `preview-3` for the others. For each branch, open *Connect* and copy the direct (not pooled) connection string, `postgresql://...`. The setup script converts it to the form the .NET driver needs. The two branches have different hosts.

2. **Google Cloud.** Use the project that already holds the OAuth client (project number 42634988358) or create a new one, and make sure billing is linked. Open Cloud Shell in that project and run the setup script (if the repository is private, upload `deploy/setup-gcp.sh` through Cloud Shell's *Upload* menu instead of cloning):

   ```sh
   git clone https://github.com/jonathanclausen/firecalc && cd firecalc
   bash deploy/setup-gcp.sh <project-id>
   ```

   It enables the needed APIs and creates the image registry, the service accounts with only the roles they need, a database secret per environment (it asks for each connection string without echoing it and converts it), placeholder preview services and the GitHub sign-in. It sets up 3 preview slots; for another number, run it as `PREVIEW_SLOTS=2 bash deploy/setup-gcp.sh <project-id>`. It can be re-run safely and only asks for what is missing. At the end it prints the values for the next steps.

3. **GitHub.** In the repository's *Settings > Secrets and variables > Actions*, add the variables `GCP_PROJECT_ID`, `GCP_WIF_PROVIDER` and `PREVIEW_SLOTS` (and `GCP_REGION` if you picked another region), and the secret `ALLOWED_EMAILS` with your Google address. Under *Issues > Labels*, add a label named `preview`. The deploy workflow skips itself until `GCP_PROJECT_ID` is set, and uses a single preview slot until `PREVIEW_SLOTS` is set.

   Optional, to reset a preview's database whenever its slot changes hands: in Neon, create an API key under *Account settings > API keys* and add it as the secret `NEON_API_KEY`, and add the project id from *Project settings* as the variable `NEON_PROJECT_ID`.

4. **Deploy.** Run *Actions > Deploy > Run workflow* on `main` (later pushes to `main` deploy on their own). The run summary shows the address, `https://firecalc-web-<project number>.europe-west1.run.app`.

5. **Sign-in and budget.** In *Google Auth Platform > Clients*, open the FireCalc web client and add the production address and every preview address (the script prints them) under *Authorized JavaScript origins*. Under *Billing > Budgets & alerts*, add a small budget (for example 50 kr) with email alerts.

## Day to day

- Preview a branch: open a pull request for it. If all slots are busy, add the `preview` label to take one.
- Fresh preview data: in Neon, open the preview's branch (`dev`, `preview-2`, …) and choose *Reset from parent* to copy production's current data over it.
- Change a database password: `gcloud secrets delete firecalc-db` (or `firecalc-db-preview`, `firecalc-db-preview-2`, …), re-run the setup script and paste the new connection string, then re-run the Deploy workflow.
- Logs: *Cloud Run > service > Logs*.
- Roll back: *Cloud Run > service > Revisions*, send traffic to an earlier revision.
