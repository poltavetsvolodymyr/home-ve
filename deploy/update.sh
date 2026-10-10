#!/usr/bin/env bash
# Brings this host to the latest build of its channel and applies it. Run as root (home-update.service does):
#   /opt/home-ve/deploy/update.sh
#
# Channels (docs/deployment.md, "Updating"):
#   stable  the branch "stable": the latest release (a vX.Y.Z tag). The default.
#   dev     the branch "main": every commit, as soon as it is pushed.
# The web UI writes the channel into /var/lib/home-backend/update-channel; that file belongs to the backend's
# user, so only the words "stable" and "dev" are taken from it, anything else counts as stable.
#
# A host never goes back on its own: when it already runs something newer than its channel (it was on dev,
# now it is on stable), it stays where it is until the channel catches up.
set -euo pipefail

DEPLOY_DIR="$(cd "$(dirname "$0")" && pwd)"
CHANNEL_FILE=/var/lib/home-backend/update-channel
cd "$DEPLOY_DIR"

channel=stable
if [[ -f $CHANNEL_FILE ]]; then
  wanted=$(head -c 16 "$CHANNEL_FILE" | tr -d '[:space:]')
  case $wanted in
    stable | dev) channel=$wanted ;;
    *) echo "note: $CHANNEL_FILE says '$wanted', which is no channel: using stable" >&2 ;;
  esac
fi
case $channel in
  stable) branch=stable ;;
  dev) branch=main ;;
esac

# --exit-code: 2 when the branch isn't there (before the first release), other errors (no network) stay errors
rc=0
git ls-remote --quiet --exit-code --heads origin "$branch" >/dev/null || rc=$?
if [[ $rc == 2 ]]; then
  echo "channel $channel: there is no branch '$branch' upstream yet (no release so far): nothing to update to"
  exit 0
fi
[[ $rc == 0 ]] || exit "$rc"
# only the channel's branch, plus the release tags (for the version shown in the web UI)
git fetch --quiet --tags --force origin "+refs/heads/$branch:refs/remotes/origin/$branch"
target=$(git rev-parse "refs/remotes/origin/$branch")
before=$(git rev-parse HEAD)
version() { git describe --tags --match 'v[0-9]*' --always "$1"; }

if [[ $before == "$target" ]]; then
  # an update that stopped at downloading its build (deploy/fetch-build.sh) is finished now
  if [[ "$(cat app/COMMIT 2>/dev/null)" != "$before" ]]; then
    echo "channel $channel: up to date ($(version HEAD)), but its build isn't installed yet: installing it"
    exec "$DEPLOY_DIR/install.sh"
  fi
  echo "channel $channel: already up to date ($(version HEAD))"
  exit 0
fi
if git merge-base --is-ancestor "$target" "$before"; then
  echo "channel $channel is at $(version "$target"), this host runs the newer $(version HEAD): it stays on that until $channel catches up"
  exit 0
fi

# CI builds a commit a few minutes after it is pushed (or released): until then, nothing changes here
if ! "$DEPLOY_DIR/fetch-build.sh" check "$target"; then
  echo "channel $channel: $(version "$target") is out, but its build isn't ready to download yet (CI makes it" >&2
  echo "a few minutes after a push or a release). Nothing changed here: try again in a few minutes." >&2
  exit 1
fi

echo "channel $channel: $(version "$before") -> $(version "$target")"
# the branch is moved to the target, whatever it was: hosts don't commit, and history rewritten upstream
# (or a switch of channel) must not stop updates
git checkout --quiet -B "$branch" "$target"
git branch --quiet --set-upstream-to="origin/$branch" "$branch"
if git merge-base --is-ancestor "$before" "$target"; then
  git --no-pager log --oneline "$before..$target"
fi

# unit file, config migrations, frontend, restart: install.sh is idempotent and does all of it.
# exec: run the install.sh that just arrived, not this (already running, possibly older) script.
exec "$DEPLOY_DIR/install.sh"
