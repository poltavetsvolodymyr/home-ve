#!/usr/bin/env bash
# package.sh <name>.tar.gz: what build.sh put into deploy/app and deploy/www, as the archive hosts download
# (deploy/fetch-build.sh), and <name>.tar.gz.sha256 next to it. Run by the workflows after build.sh.
set -euo pipefail

name=${1:?usage: package.sh <name>.tar.gz}
root="$(cd "$(dirname "$0")/.." && pwd)"
[[ -x $root/deploy/app/home-backend && -f $root/deploy/www/index.html && -f $root/deploy/app/COMMIT ]] ||
  { echo "package.sh: run build.sh first" >&2; exit 1; }

# the same owner and times for every file: the archive is about the content
tar -czf "$name" -C "$root/deploy" --owner=0 --group=0 --numeric-owner --sort=name \
  --mtime="@$(git -C "$root" log -1 --format=%ct)" app www
sha256sum "$name" | sed 's#  .*/#  #' >"$name.sha256"
cat "$name.sha256"
