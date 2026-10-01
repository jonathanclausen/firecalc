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

echo "== Image registry (keeps the 2 newest images of each service and environment)"
if ! gcloud artifacts repositories describe firecalc --location="$REGION" >/dev/null 2>&1; then
  gcloud artifacts repositories create firecalc --repository-format=docker --location="$REGION"
fi
policy=$(mktemp)
cat > "$policy" <<'JSON'
[
  {"name": "keep-newest", "action": {"type": "Keep"}, "mostRecentVersions": {"keepCount": 2}},
  {"name": "delete-rest", "action": {"type": "Delete"}, "condition": {"tagState": "any"}}
]
JSON
gcloud artifacts repositories set-cleanup-policies firecalc --location="$REGION" \
  --policy="$policy" --no-dry-run >/dev/null
rm "$policy"

echo "== Service accounts"
# deploy / deploy-preview: used by GitHub Actions. api / api-preview: run the APIs, each reading only
# its own database secret. web: runs both web services and may call the APIs.
for name in deploy deploy-preview api api-preview web; do
  if ! gcloud iam service-accounts describe "$(sa "$name")" >/dev/null 2>&1; then
    gcloud iam service-accounts create "firecalc-$name" --display-name="FireCalc $name"
  fi
done

# Turns Neon's postgresql://user:password@host/db?sslmode=require URL (or its "psql '...'" form)
# into the key=value form the .NET driver needs. A string already in that form is kept as is.
to_npgsql() {
  local url=$1 re='^postgres(ql)?://([^:@/]+)(:([^@/]*))?@([^/:?]+)(:([0-9]+))?/([^?]+)'
  url=${url#psql } url=${url#\'} url=${url%\'} url=${url#\"} url=${url%\"}
  if [[ $url == Host=* ]]; then printf %s "$url"; return; fi
  if [[ ! $url =~ $re ]]; then
    echo "That doesn't look like a postgresql:// connection string." >&2
    return 1
  fi
  # URL-decode the user and password (%40 -> @ and so on).
  local user password
  user=$(printf '%b' "${BASH_REMATCH[2]//%/\\x}")
  password=$(printf '%b' "${BASH_REMATCH[4]//%/\\x}")
  printf 'Host=%s;Port=%s;Database=%s;Username=%s;Password="%s";SSL Mode=Require' \
    "${BASH_REMATCH[5]}" "${BASH_REMATCH[7]:-5432}" "${BASH_REMATCH[8]}" "$user" "${password//\"/\"\"}"
}

# Stores a connection string, asking for it without echoing. $1 = secret, $2 = who may read it.
db_secret() {
  if ! gcloud secrets describe "$1" >/dev/null 2>&1; then
    local db
    read -rsp "$3, as Neon shows it (postgresql://...), then Enter: " db
    echo
    db=$(to_npgsql "$db")
    printf %s "$db" | gcloud secrets create "$1" --replication-policy=automatic --data-file=-
    unset db
  else
    echo "$1 already exists; to replace it, run: gcloud secrets delete $1, then this script again"
  fi
  gcloud secrets add-iam-policy-binding "$1" \
    --member="serviceAccount:$2" --role=roles/secretmanager.secretAccessor >/dev/null
}

echo "== Database connection strings (Secret Manager)"
db_secret firecalc-db "$(sa api)" "Paste the Neon PRODUCTION connection string"
db_secret firecalc-db-preview "$(sa api-preview)" "Paste the Neon DEV branch connection string"

echo "== Preview services (placeholders until the first preview deploy)"
for service in api web; do
  if ! gcloud run services describe "firecalc-$service-preview" --region="$REGION" >/dev/null 2>&1; then
    if [ "$service" = api ]; then runtime=$(sa api-preview); else runtime=$(sa web); fi
    gcloud run deploy "firecalc-$service-preview" --region="$REGION" \
      --image=us-docker.pkg.dev/cloudrun/container/hello \
      --service-account="$runtime" --no-allow-unauthenticated --max-instances=1 --quiet
  fi
done

echo "== Permissions"
# The web services may call the private APIs.
gcloud projects add-iam-policy-binding "$PROJECT_ID" \
  --member="serviceAccount:$(sa web)" --role=roles/run.invoker --condition=None >/dev/null
# Production deploys may manage any Cloud Run service; preview deploys only the two preview ones.
gcloud projects add-iam-policy-binding "$PROJECT_ID" \
  --member="serviceAccount:$(sa deploy)" --role=roles/run.admin --condition=None >/dev/null
for service in api web; do
  gcloud run services add-iam-policy-binding "firecalc-$service-preview" --region="$REGION" \
    --member="serviceAccount:$(sa deploy-preview)" --role=roles/run.admin >/dev/null
done
for deployer in deploy deploy-preview; do
  gcloud artifacts repositories add-iam-policy-binding firecalc --location="$REGION" \
    --member="serviceAccount:$(sa "$deployer")" --role=roles/artifactregistry.writer >/dev/null
done
# Each deploy account may run services as the identities its environment uses.
for pair in deploy:api deploy:web deploy-preview:api-preview deploy-preview:web; do
  gcloud iam service-accounts add-iam-policy-binding "$(sa "${pair#*:}")" \
    --member="serviceAccount:$(sa "${pair%%:*}")" --role=roles/iam.serviceAccountUser >/dev/null
done

echo "== GitHub sign-in (Workload Identity Federation for $GITHUB_REPO)"
if ! gcloud iam workload-identity-pools describe github --location=global >/dev/null 2>&1; then
  gcloud iam workload-identity-pools create github --location=global --display-name="GitHub Actions"
fi
# shellcheck disable=SC2054 # the commas belong to the attribute mapping
provider_args=(
  --location=global --workload-identity-pool=github
  --display-name="GitHub"
  --attribute-mapping=google.subject=assertion.sub,attribute.repository=assertion.repository,attribute.ref=assertion.ref
  --attribute-condition="assertion.repository == '$GITHUB_REPO'"
)
if gcloud iam workload-identity-pools providers describe github \
  --location=global --workload-identity-pool=github >/dev/null 2>&1; then
  gcloud iam workload-identity-pools providers update-oidc github "${provider_args[@]}" >/dev/null
else
  gcloud iam workload-identity-pools providers create-oidc github "${provider_args[@]}" \
    --issuer-uri=https://token.actions.githubusercontent.com
fi
pool="projects/$PROJECT_NUMBER/locations/global/workloadIdentityPools/github"
# Only workflows running on main may deploy production; any branch of the repo may deploy preview.
gcloud iam service-accounts add-iam-policy-binding "$(sa deploy)" \
  --role=roles/iam.workloadIdentityUser \
  --member="principalSet://iam.googleapis.com/$pool/attribute.ref/refs/heads/main" >/dev/null
gcloud iam service-accounts add-iam-policy-binding "$(sa deploy-preview)" \
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

Then add both addresses to the OAuth client's "Authorized JavaScript origins":

    https://firecalc-web-$PROJECT_NUMBER.$REGION.run.app
    https://firecalc-web-preview-$PROJECT_NUMBER.$REGION.run.app

DONE
