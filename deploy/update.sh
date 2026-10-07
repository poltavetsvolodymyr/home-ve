#!/usr/bin/env bash
# Pulls the latest build and applies it. Run as root on the host "home":
#   /opt/home-ve/deploy/update.sh
set -euo pipefail

DEPLOY_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$DEPLOY_DIR"

before=$(git rev-parse HEAD)
git pull --ff-only
after=$(git rev-parse HEAD)

if [[ "$before" == "$after" ]]; then
  echo "already up to date ($after)"
  exit 0
fi

git --no-pager log --oneline "$before..$after"

# unit file, config migrations, frontend, restart: install.sh is idempotent and does all of it.
# exec: run the install.sh that just arrived, not this (already running, possibly older) script.
exec "$DEPLOY_DIR/install.sh"
