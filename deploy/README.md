# Deploying to Google Cloud

FireCalc runs as two Cloud Run services in `europe-west1` (Belgium, close to Neon's Frankfurt region), with the database on Neon's free Postgres tier. GitHub Actions builds and deploys them (`.github/workflows/deploy.yml`):

| | Production | Preview |
|---|---|---|
| Deploys | every push to `main` | a pull request labelled `preview` (and each push to it), or *Run workflow* on any other branch |
| Services | `firecalc-web`, `firecalc-api` | `firecalc-web-preview`, `firecalc-api-preview` |
| Database | Neon `main` branch (secret `firecalc-db`) | Neon `dev` branch (secret `firecalc-db-preview`) |

There is one preview environment, so the latest preview deploy replaces the previous one. Its address is shown on the pull request under *Deployments*.

```
browser ──▶ firecalc-web (public, Angular SSR) ──/api──▶ firecalc-api (private, .NET) ──▶ Neon Postgres
```

- **firecalc-web** is the only public service. It renders pages and forwards `/api` to the API, adding a Google-signed token for its own identity in `X-Serverless-Authorization`, so the user's Firebase token still travels in `Authorization`.
- **firecalc-api** refuses any caller that isn't the web service, then checks the user's Firebase token (and, while sign-up is closed, the email allow-list). It applies database migrations on start-up, so a branch with a new migration changes only the dev database until it reaches `main`.
- Both scale to zero when idle, so the first request after a quiet spell takes a few seconds while Cloud Run and Neon wake up.
- GitHub signs in to Google Cloud with Workload Identity Federation, so no service account key exists anywhere. Only workflows running on `main` can use the production deploy account; branches get a separate account that can only change the preview services, and the preview API can only read the dev database secret.

## Cost

Expected to stay at 0 kr for personal use: Cloud Run's free tier (2 million requests and 180,000 vCPU-seconds a month) covers both environments, Neon's free plan includes branches, and the image registry keeps only the 2 newest images per service and environment (a few cents a month at most if it passes the 0.5 GB free storage). Set a budget alert anyway (step 5).

## One-time setup

1. **Neon.** Create a project at [neon.tech](https://neon.tech) in **AWS Europe Central 1 (Frankfurt)**, then add a branch named `dev` under *Branches*. For each branch, open *Connect* and copy the direct (not pooled) connection string, `postgresql://...`. The setup script converts it to the form the .NET driver needs. The two branches have different hosts.

2. **Google Cloud.** Use the project that already holds the OAuth client (project number 42634988358) or create a new one, and make sure billing is linked. Open Cloud Shell in that project and run the setup script (if the repository is private, upload `deploy/setup-gcp.sh` through Cloud Shell's *Upload* menu instead of cloning):

   ```sh
   git clone https://github.com/jonathanclausen/firecalc && cd firecalc
   bash deploy/setup-gcp.sh <project-id>
   ```

   It enables the needed APIs and creates the image registry, the service accounts with only the roles they need, the two database secrets (it asks for each connection string without echoing it and converts it), placeholder preview services and the GitHub sign-in. It can be re-run safely. At the end it prints the values for the next steps.

3. **GitHub.** In the repository's *Settings > Secrets and variables > Actions*, add the variables `GCP_PROJECT_ID` and `GCP_WIF_PROVIDER` (and `GCP_REGION` if you picked another region), and the secret `ALLOWED_EMAILS` with your Google address (and optionally `ADMIN_EMAILS` for who sees the admin page; without it the allowed addresses are admins). Under *Issues > Labels*, add a label named `preview`. The deploy workflow skips itself until `GCP_PROJECT_ID` is set.

4. **Deploy.** Run *Actions > Deploy > Run workflow* on `main` (later pushes to `main` deploy on their own). The run summary shows the address, `https://firecalc-web-<project number>.europe-west1.run.app`.

5. **Sign-in.** Set up Firebase Authentication as described in [Sign-in](#sign-in-firebase) below.

6. **Budget.** Under *Billing > Budgets & alerts*, add a small budget (for example 50 kr) with email alerts.

## Own domain (okonomi.concensur.dk)

Production is also served at `https://okonomi.concensur.dk` through a Cloud Run domain mapping (available in `europe-west1`); the `run.app` address keeps working. The preview environment has no own domain. The deploy workflow adds the domain to the hosts the web server answers for; the repository variable `CUSTOM_DOMAIN` changes it (`none` turns it off).

1. **DNS.** At Simply.com, `concensur.dk` has the record `okonomi CNAME ghs.googlehosted.com`. It takes precedence over the zone's `*` A record.
2. **Prove you own the domain.** Domain mappings need the domain verified in Search Console by the Google account gcloud uses. Check with
   ```sh
   gcloud domains list-user-verified
   ```
   If `concensur.dk` is missing, run `gcloud domains verify concensur.dk`, add the TXT record Search Console shows at the zone apex, and click *Verify*.
3. **Map it.**
   ```sh
   gcloud beta run domain-mappings create --service=firecalc-web --domain=okonomi.concensur.dk --region=europe-west1 --project=firecalc-510316
   ```
   Google then issues the HTTPS certificate, which can take from a few minutes to a day. `gcloud beta run domain-mappings describe --domain=okonomi.concensur.dk --region=europe-west1 --project=firecalc-510316` shows its progress.
4. **Sign-in.** In Firebase *Authentication > Settings > Authorized domains*, add `okonomi.concensur.dk`. Google and Facebook logins open a popup on `firecalc-510316.firebaseapp.com`, so their redirect settings stay as they are.

## Sign-in (Firebase)

People sign in with Google, Facebook, or email and password through Firebase Authentication in the same Google Cloud project. Firebase sends the verification and password-reset emails and keeps one user per email address, so the different logins of one person reach the same data. It is free for this kind of sign-in (no SMS).

1. **Add Firebase to the project.** At [console.firebase.google.com](https://console.firebase.google.com), choose *Create a project* > *Add Firebase to Google Cloud project* and pick `firecalc-510316`. Analytics isn't needed.
2. **Turn on the logins.** *Build > Authentication > Get started*, then on *Sign-in method*:
   - *Email/Password*: enable the first switch only (not "Email link").
   - *Google*: enable and pick your support email.
   - *Facebook*: enable and paste the App ID and App secret from step 4. Copy the *OAuth redirect URI* it shows.
3. **Settings.** On Authentication's *Settings* tab:
   - *Authorized domains*: add `firecalc-web-42634988358.europe-west1.run.app` and `firecalc-web-preview-42634988358.europe-west1.run.app`.
   - *User account linking*: keep *Link accounts that use the same email*.
4. **Facebook app.** At [developers.facebook.com/apps](https://developers.facebook.com/apps), *Create app* with the use case *Authenticate and request data from users with Facebook Login*. Under *Facebook Login > Settings*, put the redirect URI from step 2 in *Valid OAuth Redirect URIs*. *App settings > Basic* has the App ID and secret. While the app is unpublished only people with a role on it can sign in with Facebook; publishing needs a privacy policy URL and a data deletion URL.
5. **Web config.** *Project settings > General > Your apps > Add app > Web*, name it FireCalc (no Hosting). Copy `apiKey` from the config it shows and add it in GitHub as the repository variable `FIREBASE_API_KEY`. The key is public; it only names the project.
6. **Who gets in.** Anyone with a verified email can sign up. To close sign-up again, set the repository variable `OPEN_SIGNUP` to `false`; then only the addresses in the `ALLOWED_EMAILS` secret get in. Re-run the Deploy workflow after changing either variable.

Existing data carries over: the first time someone signs in through Firebase with a verified email that already has a FireCalc user, that user is taken over.

## Day to day

- Preview a branch: add the `preview` label to its pull request, or run *Actions > Deploy > Run workflow* and pick the branch.
- Fresh dev data: in Neon, open the `dev` branch and choose *Reset from parent* to copy production's current data over it.
- Change a database password: `gcloud secrets delete firecalc-db` (or `firecalc-db-preview`), re-run the setup script and paste the new connection string, then re-run the Deploy workflow.
- Logs: *Cloud Run > service > Logs*.
- Roll back: *Cloud Run > service > Revisions*, send traffic to an earlier revision.
