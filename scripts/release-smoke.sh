#!/usr/bin/env bash

set -euo pipefail

BASE_URL="${1:-http://localhost:5100}"
ADMIN_EMAIL="${2:-admin@example.com}"
ADMIN_PASSWORD="${3:-changeme}"

COOKIE_FILE="$(mktemp)"
BODY_FILE="$(mktemp)"

cleanup() {
  rm -f "$COOKIE_FILE" "$BODY_FILE"
}
trap cleanup EXIT

request_code() {
  local method="$1"
  local url="$2"
  local expected="$3"
  local data="${4:-}"

  local code

  if [[ -n "$data" ]]; then
    code="$(curl -sS -o "$BODY_FILE" -w "%{http_code}" -X "$method" "$url" -H "Content-Type: application/json" -c "$COOKIE_FILE" -b "$COOKIE_FILE" -d "$data")"
  else
    code="$(curl -sS -o "$BODY_FILE" -w "%{http_code}" -X "$method" "$url" -c "$COOKIE_FILE" -b "$COOKIE_FILE")"
  fi

  if [[ "$code" != "$expected" ]]; then
    echo "[FAIL] $method $url -> $code (expected $expected)"
    echo "Response body:"
    cat "$BODY_FILE"
    exit 1
  fi

  echo "[OK]   $method $url -> $code"
}

request_code_any() {
  local method="$1"
  local url="$2"
  shift 2
  local accepted=("$@")

  local code
  code="$(curl -sS -o "$BODY_FILE" -w "%{http_code}" -X "$method" "$url" -c "$COOKIE_FILE" -b "$COOKIE_FILE")"

  for status in "${accepted[@]}"; do
    if [[ "$code" == "$status" ]]; then
      echo "[OK]   $method $url -> $code"
      return
    fi
  done

  echo "[FAIL] $method $url -> $code (expected one of: ${accepted[*]})"
  echo "Response body:"
  cat "$BODY_FILE"
  exit 1
}

echo "Running release smoke checks against: $BASE_URL"

request_code "GET" "$BASE_URL/health" "200"
request_code "GET" "$BASE_URL/metrics" "200"

LOGIN_PAYLOAD="{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}"
request_code "POST" "$BASE_URL/api/v1/auth/login" "200" "$LOGIN_PAYLOAD"

request_code "GET" "$BASE_URL/api/v1/auth/me" "200"
request_code "GET" "$BASE_URL/api/v1/dashboard/overview" "200"
request_code "GET" "$BASE_URL/api/v1/dashboard/timeline?period=24h&interval=15m" "200"

# In production this endpoint is typically 404; in local dev/debug builds it can be 200.
request_code_any "GET" "$BASE_URL/api/v1/dashboard/dev/traffic/status" "404" "200"

echo "Smoke checks completed successfully."
