#!/usr/bin/env bash
# Orchestrates the benchmark stack and runs k6 scenarios.
#
#   tests/benchmark/run-bench.sh up                    # start stack + seed
#   tests/benchmark/run-bench.sh down                  # tear down
#   tests/benchmark/run-bench.sh seed                  # (re)seed app/endpoint
#   tests/benchmark/run-bench.sh single [RATE] [DUR]   # single-send k6 run
#   tests/benchmark/run-bench.sh batch  [RATE] [DUR]   # batch-send k6 run
#   tests/benchmark/run-bench.sh mixed  [SR] [LR] [DUR]
#   tests/benchmark/run-bench.sh all                   # single + batch + mixed
#
# Results land in tests/benchmark/results/<scenario>-<timestamp>.json plus a
# stdout summary.
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
COMPOSE="$DIR/docker-compose.bench.yml"
RESULTS="$DIR/results"
SECRETS="$DIR/.bench.env"

mkdir -p "$RESULTS"

cmd="${1:-help}"
shift || true

up() {
  docker compose -f "$COMPOSE" up -d --build
  echo "Waiting for /health…"
  for _ in $(seq 1 60); do
    if curl -fsS http://localhost:5100/health >/dev/null 2>&1; then
      echo "ready."
      seed
      return
    fi
    sleep 1
  done
  echo "API never came up." >&2
  docker compose -f "$COMPOSE" logs --tail 80 webhook-engine
  exit 1
}

down() {
  docker compose -f "$COMPOSE" down -v
}

seed() {
  bash "$DIR/seed.sh" > "$SECRETS"
  echo "seeded → $SECRETS"
  cat "$SECRETS"
}

run_k6() {
  local script="$1"; shift
  local label="$1"; shift
  local stamp
  stamp="$(date +%Y%m%d-%H%M%S)"
  local out="$RESULTS/${label}-${stamp}.json"

  if [[ ! -f "$SECRETS" ]]; then
    echo "missing $SECRETS — run 'up' or 'seed' first." >&2
    exit 1
  fi

  # Read API_KEY from .bench.env without leaking it to the parent shell history.
  # shellcheck disable=SC1090
  source "$SECRETS"

  docker run --rm -i \
    --add-host=host.docker.internal:host-gateway \
    -v "$DIR/k6:/scripts" \
    -e API_BASE="http://host.docker.internal:5100" \
    -e API_KEY="$API_KEY" \
    "$@" \
    grafana/k6 run --summary-export "/scripts/${label}-${stamp}.json" "/scripts/${script}"

  if [[ -f "$DIR/k6/${label}-${stamp}.json" ]]; then
    mv "$DIR/k6/${label}-${stamp}.json" "$out"
    echo "summary → $out"
  fi
}

case "$cmd" in
  up) up ;;
  down) down ;;
  seed) seed ;;
  single)
    rate="${1:-500}"; dur="${2:-60s}"
    run_k6 single-send.js "single-r${rate}" -e RATE="$rate" -e DURATION="$dur"
    ;;
  batch)
    rate="${1:-50}"; dur="${2:-60s}"
    run_k6 batch-send.js "batch-r${rate}" -e RATE="$rate" -e DURATION="$dur"
    ;;
  mixed)
    sr="${1:-300}"; lr="${2:-50}"; dur="${3:-60s}"
    run_k6 mixed.js "mixed-s${sr}-l${lr}" -e SEND_RATE="$sr" -e LIST_RATE="$lr" -e DURATION="$dur"
    ;;
  all)
    "$0" single
    "$0" batch
    "$0" mixed
    ;;
  *)
    sed -n '2,15p' "$0"
    ;;
esac
