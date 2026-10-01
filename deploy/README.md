# Deploying to Google Cloud

FireCalc runs as two Cloud Run services in `europe-west1` (Belgium, close to Neon's Frankfurt region), with the database on Neon's free Postgres tier. GitHub Actions builds and deploys both on every push to `main` (`.github/workflows/deploy.yml`).

```
browser ──▶ firecalc-web (public, Angular SSR) ──/api──▶ firecalc-api (private, .NET) ──▶ Neon Postgres
```

- **firecalc-web** is the only public service. It renders pages and forwards `/api` to the API, adding a Google-signed token for its own identity in `X-Serverless-Authorization`, so the user's Google token still travels in `Authorization`.
- **firecalc-api** refuses any caller that isn't the web service, then checks the user's Google token and the email allow-list as usual. It applies database migrations on start-up.
- Both scale to zero when idle, so the first request after a quiet spell takes a few seconds while Cloud Run and Neon wake up.
- GitHub signs in to Google Cloud with Workload Identity Federation, so no service account key exists anywhere. Only workflows on `main` of this repository are accepted.

## Cost

Expected to stay at 0 kr for personal use: Cloud Run's free tier (2 million requests and 180,000 vCPU-seconds a month) covers it, the image registry keeps only the 3 newest images per service to stay under its 0.5 GB free storage, and Secret Manager holds one secret. Set a budget alert anyway (step 5).

## One-time setup

1. **Neon.** Create a project at [neon.tech](https://neon.tech) in **AWS Europe Central 1 (Frankfurt)**. Under *Connect*, take the direct (not pooled) connection details and write them in .NET form:

   ```
   Host=ep-xxxx.eu-central-1.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=xxxx;SSL Mode=Require
   ```

   The `postgresql://...` URL form does not work with the .NET driver.

2. **Google Cloud.** Use the project that already holds the OAuth client (project number 42634988358) or create a new one, and make sure billing is linked. Open Cloud Shell in that project and run the setup script (if the repository is private, upload `deploy/setup-gcp.sh` through Cloud Shell's *Upload* menu instead of cloning):

   ```sh
   git clone https://github.com/jonathanclausen/firecalc && cd firecalc
   bash deploy/setup-gcp.sh <project-id>
   ```

   It enables the needed APIs and creates the image registry, three service accounts (deploy, api, web) with only the roles they need, the `firecalc-db` secret (it asks for the connection string without echoing it) and the GitHub sign-in. It can be re-run safely. At the end it prints the values for the next two steps.

3. **GitHub.** In the repository's *Settings > Secrets and variables > Actions*, add the variables `GCP_PROJECT_ID` and `GCP_WIF_PROVIDER` (and `GCP_REGION` if you picked another region), and the secret `ALLOWED_EMAILS` with your Google address. The deploy workflow skips itself until `GCP_PROJECT_ID` is set.

4. **Deploy.** Run *Actions > Deploy > Run workflow* on `main` (later pushes to `main` deploy on their own). The run summary shows the address, `https://firecalc-web-<project number>.europe-west1.run.app`.

5. **Sign-in and budget.** In *Google Auth Platform > Clients*, open the FireCalc web client and add that address under *Authorized JavaScript origins*. Under *Billing > Budgets & alerts*, add a small budget (for example 50 kr) with email alerts.

## Day to day

- Change the database password: `gcloud secrets versions add firecalc-db --data-file=-`, paste, Ctrl-D, then re-run the Deploy workflow.
- Logs: *Cloud Run > firecalc-api (or firecalc-web) > Logs*.
- Roll back: *Cloud Run > service > Revisions*, send traffic to an earlier revision.
