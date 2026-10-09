#!/usr/bin/env bash
# Sets up latency monitoring for FireCalc: a log-based metric with each API endpoint's latency and a
# Cloud Monitoring dashboard with p50/p95/p99. Run it in Cloud Shell from the repository root:
#
#   bash deploy/setup-monitoring.sh <project-id>
#
# Safe to run again: the metric and the dashboard are updated in place, so the dashboard keeps its link.
# The deploy workflow also runs it on each production deploy (with SKIP_ENABLE=1), which needs the deploy
# account to have the roles Logs Configuration Writer and Monitoring Dashboard Configuration Editor.
set -euo pipefail

PROJECT_ID=${1:?usage: setup-monitoring.sh <project-id>}
DIR=$(cd "$(dirname "$0")/monitoring" && pwd)
METRIC=firecalc_api_latency
DASHBOARD="FireCalc latency"

gcloud config set project "$PROJECT_ID" >/dev/null
if [[ -z ${SKIP_ENABLE:-} ]]; then
  gcloud services enable logging.googleapis.com monitoring.googleapis.com
fi

echo "== Log-based metric $METRIC"
if gcloud logging metrics describe "$METRIC" >/dev/null 2>&1; then
  gcloud logging metrics update "$METRIC" --config-from-file="$DIR/api-latency-metric.yaml" >/dev/null
else
  gcloud logging metrics create "$METRIC" --config-from-file="$DIR/api-latency-metric.yaml" >/dev/null
fi

echo "== Dashboard \"$DASHBOARD\""
name=$(gcloud monitoring dashboards list --filter="displayName=\"$DASHBOARD\"" --format='value(name)' --limit=1)
if [[ -n $name ]]; then
  gcloud monitoring dashboards update "$name" --config-from-file="$DIR/dashboard.json" >/dev/null
else
  name=$(gcloud monitoring dashboards create --config-from-file="$DIR/dashboard.json" --format='value(name)')
fi
echo
echo "Done. The per-endpoint chart fills in as requests come in (only requests from now on count)."
if [[ -n $name ]]; then
  echo "Dashboard: https://console.cloud.google.com/monitoring/dashboards/builder/${name##*/}?project=$PROJECT_ID"
else
  echo "Dashboards: https://console.cloud.google.com/monitoring/dashboards?project=$PROJECT_ID"
fi
