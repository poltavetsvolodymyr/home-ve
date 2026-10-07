#!/usr/bin/env bash
# Builds everything the host "home" runs into deploy/ and stages it in git:
#   frontend -> deploy/www  (npm run build)
#   backend  -> deploy/app  (one self-contained linux-x64 executable, the .NET runtime inside;
#                            settings in backend/HomeBackend/Properties/PublishProfiles/Home.pubxml)
# Then commit, push, and run deploy/update.sh on the host.
#   ./build.sh
# Same steps as build.ps1, for Linux and macOS.
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"

(cd "$root/frontend" && npm ci --no-fund --no-audit && npm run build)

# start clean so files removed from the project don't linger on the host
rm -rf "$root/deploy/app"
dotnet publish "$root/backend/HomeBackend" -p:PublishProfile=Home --nologo

git -C "$root" add deploy/app deploy/www
git -C "$root" update-index --chmod=+x deploy/app/home-backend

size=$(du -m "$root/deploy/app/home-backend" | cut -f1)
echo
echo "Built deploy/app/home-backend (${size} MB) and deploy/www, staged in git. Commit and push, then run deploy/update.sh on the host."
