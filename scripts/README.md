# Scripts

Utility scripts for repository setup and maintenance.

## GitHub Repository Setup

These scripts require the GitHub CLI (`gh`) and an authenticated session (`gh auth login`).

### Configure labels

```bash
./scripts/setup-github-labels.sh
```

Or target a specific repository:

```bash
./scripts/setup-github-labels.sh voyvodka/webhook-engine
```

### Configure milestones

```bash
./scripts/setup-github-milestones.sh
```

Or target a specific repository:

```bash
./scripts/setup-github-milestones.sh voyvodka/webhook-engine
```
