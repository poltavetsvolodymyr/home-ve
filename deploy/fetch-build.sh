#!/usr/bin/env bash
# The build of a commit: deploy/app (the backend, one self-contained binary) and deploy/www (the frontend).
# CI makes it from the sources (.github/workflows) and attaches it to the GitHub releases of the repository
# this checkout came from:
#   a release vX.Y.Z:            home-ve-vX.Y.Z-linux-x64.tar.gz on that release
#   every commit on main:        home-ve-<commit>-linux-x64.tar.gz on the pre-release "dev" (the latest 30)
# each with a .sha256 next to it. A release's commit has both (it was on main first): whichever is there.
#
#   fetch-build.sh check <commit>   whether its build can be downloaded (exit 0) or not yet (exit 1)
#   fetch-build.sh get <commit>     downloads it, checks it and puts it into deploy/app and deploy/www
#
# A build already in deploy/ for that commit (deploy/app/COMMIT says which; build.sh writes it too, for a
# build made on the host itself) is kept as it is. BUILDS_URL instead of GitHub: a mirror, or tests.
set -euo pipefail

DEPLOY_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT=${DEPLOY_DIR%/deploy}

die() {
  echo "fetch-build: $*" >&2
  exit 1
}

mode=${1:-}
if [[ $mode != check && $mode != get ]] || [[ -z ${2:-} ]]; then die "usage: fetch-build.sh check|get <commit>"; fi
commit=$(git -C "$ROOT" rev-parse --verify --quiet "$2^{commit}") || die "no commit '$2' here"

# https://github.com/<owner>/<repo>/releases/download, from where the checkout came from (https or ssh)
builds_url() {
  if [[ -n ${BUILDS_URL:-} ]]; then
    echo "$BUILDS_URL"
    return
  fi
  local origin repo
  origin=$(git -C "$ROOT" remote get-url origin)
  repo=$(sed -E 's#^(https://|ssh://git@|git@)github\.com[:/]##; s#/$##; s#\.git$##' <<<"$origin")
  [[ $repo =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] ||
    die "the checkout comes from $origin, not a repository on GitHub: build it here with build.sh"
  echo "https://github.com/$repo/releases/download"
}

# where its build may be, in this order: the release's own, then the one of the dev pre-release
candidates() {
  local tag
  if tag=$(git -C "$ROOT" describe --exact-match --tags --match 'v[0-9]*.[0-9]*.[0-9]*' "$commit" 2>/dev/null); then
    echo "$tag/home-ve-$tag-linux-x64.tar.gz"
  fi
  echo "dev/home-ve-$commit-linux-x64.tar.gz"
}

url=$(builds_url)
# -f: an HTTP error is a failure; -L: GitHub answers with a redirect to where the file is
fetch() { curl -fsSL --retry 3 --connect-timeout 15 --max-time 900 "$@"; }

if [[ $mode == check ]]; then
  # the .sha256 goes up after the archive: once it is there, both are
  for c in $(candidates); do
    if fetch -o /dev/null "$url/$c.sha256" 2>/dev/null; then exit 0; fi
  done
  exit 1
fi

if [[ -x $DEPLOY_DIR/app/home-backend && -f $DEPLOY_DIR/www/index.html &&
  "$(cat "$DEPLOY_DIR/app/COMMIT" 2>/dev/null)" == "$commit" ]]; then
  echo "==> the build of ${commit:0:7} is already here"
  exit 0
fi

tmp=$(mktemp -d)
trap 'rm -rf -- "${tmp:?}"' EXIT
found=''
for c in $(candidates); do
  echo "==> downloading $url/$c"
  if fetch -o "$tmp/sha256" "$url/$c.sha256" && fetch -o "$tmp/build.tar.gz" "$url/$c"; then
    found=$c
    break
  fi
done
[[ -n $found ]] || die "no build of ${commit:0:7} to download yet: CI makes it a few minutes after a push or a release. Try again later, or build it here with build.sh"

# the file says "<hash>  <name>"
want=$(cut -d' ' -f1 "$tmp/sha256")
have=$(sha256sum "$tmp/build.tar.gz" | cut -d' ' -f1)
[[ $want =~ ^[0-9a-f]{64}$ && $want == "$have" ]] || die "$found: the checksum doesn't match, the download is broken"

mkdir "$tmp/x"
tar -xzf "$tmp/build.tar.gz" -C "$tmp/x" --no-same-owner
[[ -x $tmp/x/app/home-backend && -f $tmp/x/www/index.html ]] || die "$found has no app/home-backend or www/index.html"
[[ "$(cat "$tmp/x/app/COMMIT" 2>/dev/null)" == "$commit" ]] || die "$found is the build of another commit"

# each directory swapped in whole: no files of the old build left over. The running backend keeps its
# (now deleted) binary until install.sh restarts it.
for d in app www; do
  rm -rf -- "${DEPLOY_DIR:?}/$d.old"
  if [[ -e $DEPLOY_DIR/$d ]]; then mv -- "$DEPLOY_DIR/$d" "$DEPLOY_DIR/$d.old"; fi
  mv -- "$tmp/x/$d" "$DEPLOY_DIR/$d"
  rm -rf -- "${DEPLOY_DIR:?}/$d.old"
done
echo "==> build of ${commit:0:7} in place ($found)"
