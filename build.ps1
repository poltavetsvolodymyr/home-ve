# Builds everything the host "home" runs into deploy/ and stages it in git:
#   frontend -> deploy/www  (npm run build)
#   backend  -> deploy/app  (one self-contained linux-x64 executable, the .NET runtime inside;
#                            settings in backend/HomeBackend/Properties/PublishProfiles/Home.pubxml)
# Then commit, push, and run deploy/update.sh on the host.
#   .\build.ps1
# Same steps as build.sh. Exit codes are checked explicitly: with 'Stop', Windows PowerShell 5.1
# turns npm's stderr warnings into failures.
$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
$app = Join-Path $root 'deploy/app'

Push-Location (Join-Path $root 'frontend')
try {
    npm ci --no-fund --no-audit
    if ($LASTEXITCODE) { throw 'npm ci failed' }
    npm run build
    if ($LASTEXITCODE) { throw 'frontend build failed' }
} finally { Pop-Location }

# start clean so files removed from the project don't linger on the host
if (Test-Path $app) { Remove-Item -Recurse -Force $app -ErrorAction Stop }

dotnet publish (Join-Path $root 'backend/HomeBackend') -p:PublishProfile=Home --nologo
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

# Windows has no exec bit; record it in git so the host can run the file straight after `git pull`
git -C $root add deploy/app deploy/www
git -C $root update-index --chmod=+x deploy/app/home-backend
if ($LASTEXITCODE) { throw 'git update-index failed' }

$size = [math]::Round((Get-Item (Join-Path $app 'home-backend')).Length / 1MB, 1)
Write-Host "`nBuilt deploy/app/home-backend ($size MB) and deploy/www, staged in git. Commit and push, then run deploy/update.sh on the host."
