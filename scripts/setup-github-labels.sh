#!/usr/bin/env bash

set -euo pipefail

# Usage:
#   ./scripts/setup-github-labels.sh
#   ./scripts/setup-github-labels.sh voyvodka/webhook-engine

REPO="${1:-}"

if ! command -v gh >/dev/null 2>&1; then
  echo "gh CLI not found. Install from https://cli.github.com/"
  exit 1
fi

if [[ -z "$REPO" ]]; then
  REPO="$(gh repo view --json nameWithOwner -q .nameWithOwner 2>/dev/null || true)"
fi

if [[ -z "$REPO" ]]; then
  echo "Could not resolve repository. Pass explicitly: owner/repo"
  exit 1
fi

echo "Configuring labels for $REPO"

create_label() {
  local name="$1"
  local color="$2"
  local description="$3"

  gh label create "$name" \
    --repo "$REPO" \
    --color "$color" \
    --description "$description" \
    --force >/dev/null

  echo "  - $name"
}

# Core triage labels
create_label "bug" "d73a4a" "Something is not working"
create_label "enhancement" "a2eeef" "New feature or request"
create_label "documentation" "0075ca" "Documentation improvements"
create_label "question" "d876e3" "Further information is requested"
create_label "regression" "b60205" "Behavior regressed from a previously working version"

# Triage workflow labels
create_label "status: needs-triage" "ededed" "New issue waiting for triage"
create_label "status: triaged" "0e8a16" "Issue was reproduced and prioritized"
create_label "status: blocked" "5319e7" "Issue is blocked by dependency or decision"
create_label "priority: p0" "b60205" "Critical: blocks release or core workflow"
create_label "priority: p1" "fbca04" "High: important but has workaround"
create_label "priority: p2" "1d76db" "Normal: planned improvement"

# Contribution labels
create_label "good first issue" "7057ff" "Good for first-time contributors"
create_label "help wanted" "008672" "Extra attention is needed"

# WebhookEngine domain labels
create_label "api" "1d76db" "API layer and endpoints"
create_label "dashboard" "fbca04" "React dashboard issues"
create_label "worker" "0e8a16" "Background delivery workers"
create_label "sdk" "5319e7" ".NET SDK related"
create_label "infrastructure" "cfd3d7" "Docker, CI, deployment"
create_label "database" "b60205" "PostgreSQL schema and data access"
create_label "security" "b60205" "Security-related issues"
create_label "performance" "f9d0c4" "Performance or scalability"

echo "Done. Labels configured for $REPO"
