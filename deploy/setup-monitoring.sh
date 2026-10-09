#!/usr/bin/env bash
# Sets up latency monitoring for FireCalc: a log-based metric with each API endpoint's latency and a
# Cloud Monitoring dashboard with p50/p95/p99. Run it in Cloud Shell from the repository root:
#
#   bash deploy/setup-monitoring.sh <project-id>
#
# Safe to run again: the metric is updated and the dashboard replaced.
set -euo pipefail

PROJECT_ID=${1:?usage: setup-monitoring.sh <project-id>}
DIR=$(cd "$(dirname "$0")/monitoring" && pwd)
METRIC=firecalc_api_latency
DASHBOARD="FireCalc latency"

gcloud config set project "$PROJECT_ID" >/dev/null
gcloud services enable logging.googleapis.com monitoring.googleapis.com

echo "== Log-based metric $METRIC"
if gcloud logging metrics describe "$METRIC" >/dev/null 2>&1; then
  gcloud logging metrics update "$METRIC" --config-from-file="$DIR/api-latency-metric.yaml" >/dev/null
else
  gcloud logging metrics create "$METRIC" --config-from-file="$DIR/api-latency-metric.yaml" >/dev/null
fi

echo "== Dashboard \"$DASHBOARD\""
for old in $(gcloud monitoring dashboards list --filter="displayName=\"$DASHBOARD\"" --format='value(name)'); do
  gcloud monitoring dashboards delete "$old" --quiet >/dev/null
done
name=$(gcloud monitoring dashboards create --config-from-file="$DIR/dashboard.json" --format='value(name)')

echo
echo "Done. The per-endpoint chart fills in as requests come in (only requests from now on count)."
if [[ -n $name ]]; then
  echo "Dashboard: https://console.cloud.google.com/monitoring/dashboards/builder/${name##*/}?project=$PROJECT_ID"
else
  echo "Dashboards: https://console.cloud.google.com/monitoring/dashboards?project=$PROJECT_ID"
fi
