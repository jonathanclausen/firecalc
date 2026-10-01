#!/usr/bin/env bash
# One-time Google Cloud setup for FireCalc. Run it in Cloud Shell (or anywhere gcloud is
# signed in as the project owner):
#
#   bash deploy/setup-gcp.sh <project-id>
#
# It is safe to run again: anything that already exists is left alone.
# What it creates and why is described in deploy/README.md.
set -euo pipefail

PROJECT_ID=${1:?usage: setup-gcp.sh <project-id> [region]}
REGION=${2:-europe-west1}
GITHUB_REPO=jonathanclausen/firecalc

gcloud config set project "$PROJECT_ID" >/dev/null
PROJECT_NUMBER=$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')
sa() { echo "firecalc-$1@$PROJECT_ID.iam.gserviceaccount.com"; }

echo "== Enabling APIs"
gcloud services enable \
  run.googleapis.com \
  artifactregistry.googleapis.com \
  secretmanager.googleapis.com \
  iam.googleapis.com \
  iamcredentials.googleapis.com \
  sts.googleapis.com

echo "== Image registry (keeps the 3 newest images of each service)"
if ! gcloud artifacts repositories describe firecalc --location="$REGION" >/dev/null 2>&1; then
  gcloud artifacts repositories create firecalc --repository-format=docker --location="$REGION"
fi
policy=$(mktemp)
cat > "$policy" <<'JSON'
[
  {"name": "keep-newest", "action": {"type": "Keep"}, "mostRecentVersions": {"keepCount": 3}},
  {"name": "delete-rest", "action": {"type": "Delete"}, "condition": {"tagState": "any"}}
]
JSON
gcloud artifacts repositories set-cleanup-policies firecalc --location="$REGION" \
  --policy="$policy" --no-dry-run >/dev/null
rm "$policy"

echo "== Service accounts"
for name in deploy api web; do
  if ! gcloud iam service-accounts describe "$(sa "$name")" >/dev/null 2>&1; then
    gcloud iam service-accounts create "firecalc-$name" --display-name="FireCalc $name"
  fi
done

echo "== Database connection string (Secret Manager: firecalc-db)"
if ! gcloud secrets describe firecalc-db >/dev/null 2>&1; then
  read -rsp "Paste the Neon connection string (Host=...;Database=...;...), then Enter: " db
  echo
  printf %s "$db" | gcloud secrets create firecalc-db --replication-policy=automatic --data-file=-
  unset db
else
  echo "firecalc-db already exists; add a new version with: gcloud secrets versions add firecalc-db --data-file=-"
fi
gcloud secrets add-iam-policy-binding firecalc-db \
  --member="serviceAccount:$(sa api)" --role=roles/secretmanager.secretAccessor >/dev/null

echo "== Permissions"
# The web service may call the private API.
gcloud projects add-iam-policy-binding "$PROJECT_ID" \
  --member="serviceAccount:$(sa web)" --role=roles/run.invoker --condition=None >/dev/null
# The deploy account pushes images and deploys both services as their own identities.
gcloud projects add-iam-policy-binding "$PROJECT_ID" \
  --member="serviceAccount:$(sa deploy)" --role=roles/run.admin --condition=None >/dev/null
gcloud artifacts repositories add-iam-policy-binding firecalc --location="$REGION" \
  --member="serviceAccount:$(sa deploy)" --role=roles/artifactregistry.writer >/dev/null
for name in api web; do
  gcloud iam service-accounts add-iam-policy-binding "$(sa "$name")" \
    --member="serviceAccount:$(sa deploy)" --role=roles/iam.serviceAccountUser >/dev/null
done

echo "== GitHub sign-in (Workload Identity Federation, main branch of $GITHUB_REPO only)"
if ! gcloud iam workload-identity-pools describe github --location=global >/dev/null 2>&1; then
  gcloud iam workload-identity-pools create github --location=global --display-name="GitHub Actions"
fi
if ! gcloud iam workload-identity-pools providers describe github \
  --location=global --workload-identity-pool=github >/dev/null 2>&1; then
  gcloud iam workload-identity-pools providers create-oidc github \
    --location=global --workload-identity-pool=github \
    --display-name="GitHub" \
    --issuer-uri=https://token.actions.githubusercontent.com \
    --attribute-mapping=google.subject=assertion.sub,attribute.repository=assertion.repository,attribute.ref=assertion.ref \
    --attribute-condition="assertion.repository == '$GITHUB_REPO' && assertion.ref == 'refs/heads/main'"
fi
pool="projects/$PROJECT_NUMBER/locations/global/workloadIdentityPools/github"
gcloud iam service-accounts add-iam-policy-binding "$(sa deploy)" \
  --role=roles/iam.workloadIdentityUser \
  --member="principalSet://iam.googleapis.com/$pool/attribute.repository/$GITHUB_REPO" >/dev/null

cat <<DONE

Done. Next, in GitHub (Settings > Secrets and variables > Actions):

  Variables
    GCP_PROJECT_ID    $PROJECT_ID
    GCP_WIF_PROVIDER  $pool/providers/github
    GCP_REGION        $REGION        (only needed if not europe-west1)
  Secrets
    ALLOWED_EMAILS    your Google address (comma-separate several)

Then add this to the OAuth client's "Authorized JavaScript origins":

    https://firecalc-web-$PROJECT_NUMBER.$REGION.run.app

DONE
