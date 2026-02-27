#!/usr/bin/env bash

set -euo pipefail

# Usage:
#   ./scripts/setup-github-milestones.sh
#   ./scripts/setup-github-milestones.sh voyvodka/webhook-engine

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

create_milestone_if_missing() {
  local title="$1"
  local description="$2"
  local due_on="$3"

  if gh api "repos/$REPO/milestones" --paginate --jq ".[] | select(.title == \"$title\") | .title" | grep -q "$title"; then
    echo "Milestone already exists: $title"
    return
  fi

  gh api "repos/$REPO/milestones" \
    --method POST \
    --field title="$title" \
    --field description="$description" \
    --field due_on="$due_on" >/dev/null

  echo "Created milestone: $title"
}

create_milestone_if_missing \
  "v1.0 Launch" \
  "Phase 1 launch tasks: docs polish, release setup, and first public release." \
  "2026-04-30T23:59:59Z"

create_milestone_if_missing \
  "Phase 2: Traction" \
  "Post-launch stabilization and feedback-driven improvements." \
  "2026-06-30T23:59:59Z"

echo "Done. Milestones configured for $REPO"
