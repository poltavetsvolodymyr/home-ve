# Builds what a host runs from these sources into deploy/ (both directories are not in git):
#   frontend -> deploy/www  (npm run build)
#   backend  -> deploy/app  (one self-contained linux-x64 executable, the .NET runtime inside;
#                            settings in backend/HomeBackend/Properties/PublishProfiles/Home.pubxml)
# deploy/app/COMMIT names the commit built. Hosts normally download the same build, made by CI for every
# commit on main and every release (deploy/fetch-build.sh); see build.sh.
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

$commit = git -C $root rev-parse HEAD
if ($LASTEXITCODE) { throw 'git rev-parse failed' }
# no BOM, a plain line: fetch-build.sh compares it with the commit as text
[IO.File]::WriteAllText((Join-Path $app 'COMMIT'), "$commit`n")

$size = [math]::Round((Get-Item (Join-Path $app 'home-backend')).Length / 1MB, 1)
Write-Host "`nBuilt deploy/app/home-backend ($size MB) and deploy/www for $($commit.Substring(0, 7))."
