#!/usr/bin/env bash
# Seeds a benchmark application + endpoint + event type into a running
# WebhookEngine instance and prints the API key + endpoint id so the k6
# scripts can target them.
set -euo pipefail

API_BASE="${API_BASE:-http://localhost:5100}"
ADMIN_EMAIL="${ADMIN_EMAIL:-admin@bench.local}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-BenchPassword123!}"
APP_NAME="${APP_NAME:-bench-app}"

cookie_jar="$(mktemp)"
trap 'rm -f "$cookie_jar"' EXIT

login_response=$(curl -sS -c "$cookie_jar" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" \
  "$API_BASE/api/v1/auth/login")
echo "$login_response" | grep -q '"data"' || { echo "login failed: $login_response" >&2; exit 1; }

app_response=$(curl -sS -b "$cookie_jar" -H 'Content-Type: application/json' \
  -d "{\"name\":\"$APP_NAME\"}" \
  "$API_BASE/api/v1/applications")

app_id=$(echo "$app_response" | python3 -c "import json, sys; print(json.load(sys.stdin)['data']['id'])")
api_key=$(echo "$app_response" | python3 -c "import json, sys; print(json.load(sys.stdin)['data']['apiKey'])")

et_response=$(curl -sS -b "$cookie_jar" -H 'Content-Type: application/json' \
  -d "{\"appId\":\"$app_id\",\"name\":\"bench.event\"}" \
  "$API_BASE/api/v1/dashboard/event-types")
event_type_id=$(echo "$et_response" | python3 -c "import json, sys; print(json.load(sys.stdin)['data']['id'])")

ep_response=$(curl -sS -b "$cookie_jar" -H 'Content-Type: application/json' \
  -d "{\"appId\":\"$app_id\",\"url\":\"http://receiver/echo\",\"filterEventTypes\":[\"$event_type_id\"]}" \
  "$API_BASE/api/v1/dashboard/endpoints")
endpoint_id=$(echo "$ep_response" | python3 -c "import json, sys; print(json.load(sys.stdin)['data']['id'])")

cat <<EOF
APP_ID=$app_id
API_KEY=$api_key
EVENT_TYPE_ID=$event_type_id
ENDPOINT_ID=$endpoint_id
EOF
