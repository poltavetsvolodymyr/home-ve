#!/usr/bin/env bash
# Builds what a host runs from these sources into deploy/ (both directories are not in git):
#   frontend -> deploy/www  (npm run build)
#   backend  -> deploy/app  (one self-contained linux-x64 executable, the .NET runtime inside;
#                            settings in backend/HomeBackend/Properties/PublishProfiles/Home.pubxml)
# deploy/app/COMMIT names the commit built. Hosts normally download the same build, made by CI for every
# commit on main and every release (deploy/fetch-build.sh); one with deploy/app/COMMIT naming its own commit
# keeps this one instead, e.g. a build of your own changes copied over to a test host.
#   ./build.sh
# Same steps as build.ps1, for Linux and macOS. CI runs this too.
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"

(cd "$root/frontend" && npm ci --no-fund --no-audit && npm run build)

# start clean so files removed from the project don't linger on the host
rm -rf "$root/deploy/app"
dotnet publish "$root/backend/HomeBackend" -p:PublishProfile=Home --nologo
git -C "$root" rev-parse HEAD >"$root/deploy/app/COMMIT"

size=$(du -m "$root/deploy/app/home-backend" | cut -f1)
echo
echo "Built deploy/app/home-backend (${size} MB) and deploy/www for $(git -C "$root" rev-parse --short HEAD)."
if ! git -C "$root" diff --quiet HEAD -- backend frontend; then
  echo "note: with changes not committed yet: deploy/app/COMMIT names the commit they are made on"
fi
