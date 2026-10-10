# Development

## Requirements

- .NET 10 SDK
- Node.js 22 LTS (Vite 7 requires Node 20.19+ or 22.12+)
- IDE: `backend/HomeBackend.slnx` opens in Visual Studio or Rider, `frontend/` in VS Code
  (it picks up ESLint and Prettier).

## Running

Backend with fake data (`appsettings.Development.json`: mock, port 5080, password `admin`):

```powershell
dotnet run --project backend/HomeBackend --launch-profile "HomeBackend (mock)"
```

Frontend with hot reload, `/api` is proxied to the backend above:

```powershell
cd frontend
npm install
npm run dev          # http://localhost:5173
```

## Checks

| What | Command |
|---|---|
| backend tests | `dotnet test --solution backend/HomeBackend.slnx` |
| everything for the frontend: types, ESLint, Prettier, tests | `cd frontend; npm run check` |
| frontend tests only | `npm test` (or `npm run test:watch`) |
| fix formatting | `npm run format` |
| C# style | `dotnet format backend/HomeBackend.slnx --verify-no-changes` |

Run the first two rows before committing. The backend build must have no warnings,
and that includes trimming warnings (see [architecture.md](architecture.md#trimming-what-not-to-do)).

CI (`.github/workflows/ci.yml`) runs on every pull request and every push to `main`: the backend build with
warnings as errors, its tests and the trimmed publish for the host; `npm run check`; and `shellcheck` on every shell
script in `deploy/` and `.github/`. A push to `main` that passes all of them is then built for the hosts (next
section).

`dotnet test` runs through Microsoft.Testing.Platform (xunit v3 on the .NET 10 SDK requires this). This mode
is enabled in `global.json` at the repository root.

### What the tests cover

- **Backend, unit tests** (`HomeBackend.Tests/Features`, `Security`): the `.conf` format (parsing, writing,
  all validation rules), systemctl commands for actions and parsing of `systemctl show`, parsing of `/proc/<pid>`,
  the log, /sys/class/hwmon and the choice of the CPU sensor; the password hash; the list of allowed networks.
- **Backend, integration tests** (`HomeBackend.Tests/Integration`): the real application with mock data on a
  random loopback port. Login and logout, 401 without a session, the VM list, actions (400 for an unknown one, 404 for
  a nonexistent VM), saving settings with validation, the VM log, dropping the connection for a foreign network, the login
  limit, security headers. `ConsoleTests` starts a fake VNC server on a unix socket and pushes bytes
  through a real WebSocket in both directions; for a stopped VM it expects 404.
- **Frontend** (`*.test.ts` next to the code): formatting, the temperature tile, VM states and actions,
  validation of the settings form and the random MAC, mapping characters to key presses for the console, log levels.
- **`deploy/vm/vm-run`** has no automated tests; when you change it, run `shellcheck deploy/vm/vm-run` (CI does it too) and
  check by hand on the host with `systemctl restart vm@<name>` + `journalctl -u vm@<name> -n 20`.

### Console locally

With the mock backend, the Console tab looks for the socket `.data/run/vm-router/vnc.sock` (`HomeBackend:VmRuntimeDir`
in `appsettings.Development.json`). Without it you get 502, which is expected. To see a live screen on Linux,
any QEMU with VNC on this socket will do:

```bash
mkdir -p backend/HomeBackend/.data/run/vm-router
qemu-system-x86_64 -m 128 -display vnc=unix:backend/HomeBackend/.data/run/vm-router/vnc.sock
```

### Installer page locally

The installer image runs the same binary as `home-backend installer` (`backend/HomeBackend/Installer`), with its own
page, `frontend/installer.html` (`src/installer`). With a made-up machine, and the code `TEST-CODE`:

```bash
dotnet run --project backend/HomeBackend --no-launch-profile -- installer --Installer:Mock=true --Installer:Urls:0=http://localhost:5081
cd frontend && npm run dev     # then http://localhost:5173/installer.html
```

The image itself (`installer/build-live.sh`, live-build) is built by `.github/workflows/installer.yml` and lands on
the pre-release **dev** as `home-ve-installer-live-dev.iso`.

## Conventions

**General:** code, comments and documentation are in English. Indentation and the like are set by
`.editorconfig`. Comments explain "why", they do not retell the code.

**C#:**
- namespace follows the folder, file-scoped;
- API models are `sealed record`s, and they are also the JSON (camelCase);
- a feature is a folder in `Features/` (see [architecture.md](architecture.md#features));
- no reflection: everything must survive trimming.

**TypeScript/React:**
- function components, named exports, one component per file;
- a page is a folder in `src/features/`, shared code goes in `src/shared/`;
- imports through `@/`;
- formatted by Prettier (`.prettierrc.json`: no `;`, single quotes, 120 characters);
- styles are global classes, colors only from `src/styles/tokens.css`.

## Building for the host

```powershell
.\build.ps1          # Windows
./build.sh           # Linux/macOS, same steps
```

The script builds the frontend into `deploy/www`, publishes the backend with the profile
`backend/HomeBackend/Properties/PublishProfiles/Home.pubxml` into `deploy/app` (a single file of about 17 MB,
linux-x64, .NET included) and writes the commit it was built from into `deploy/app/COMMIT`. Neither directory is in
git.

Hosts don't need you to build anything: CI builds every commit for them.

- **A push to `main`** whose checks pass: the `dev-build` job in `ci.yml` runs `build.sh` and attaches
  `home-ve-<commit>-linux-x64.tar.gz` (and its `.sha256`) to the pre-release **dev** on GitHub, which keeps the newest
  30. Hosts on the dev channel take it at their next update.
- **A published release** `vX.Y.Z`: `.github/workflows/release.yml` runs the checks again, then `build.sh`, and
  attaches `home-ve-vX.Y.Z-linux-x64.tar.gz` to the release.
- **The installer ISO**: `.github/build-iso.sh` takes Debian's netboot `mini.iso` (checked against Debian's
  SHA256SUMS), adds `deploy/preseed` to its initrd and writes the image anew with the original boot records.
  Releases get `home-ve-vX.Y.Z-installer.iso` (stable channel), every push to `main` replaces
  `home-ve-installer-dev.iso` on **dev**. A release's commit was on `main` before, so its dev build
  is there too: hosts take whichever they find first.

On the host, `deploy/fetch-build.sh` downloads the build of the checked-out commit, checks its SHA-256 and that it
is the build of that commit, and puts it into `deploy/app` and `deploy/www`; `install.sh` runs it first. `update.sh`
moves a host to a new commit only once its build can be downloaded, so a host never waits for one half-updated.
The repository it downloads from is the one the checkout came from (`git remote get-url origin`): a fork gets its
own builds once its Actions run.

A build of your own on a test host: copy `deploy/app` and `deploy/www` over (with `deploy/app/COMMIT` naming the
commit the host has checked out), then run `install.sh`: it keeps a build that names its commit and downloads none.

Up to v0.4.0 the binary was committed to git; those releases still bring theirs along.

## Releasing

Hosts on the stable channel (the default) follow the branch `stable`, which points at the latest release. A release
is a commit on `main` whose CI is green (its dev build is on the pre-release **dev**):

```bash
git fetch origin main
git push origin <commit>:refs/heads/stable
```

then on GitHub, Releases → **Draft a new release**: a new tag `vX.Y.Z` with the target `stable`, and what changed:
that page is what users read before they update. Publishing it starts `release.yml`, which attaches the build.

- the version: the last number for fixes, the middle one for new features, the first one when an update needs the
  user to do something by hand;
- `stable` only ever moves forward (the push fails if it would not be a fast-forward: then something is off);
- only maintainers move `stable` and make releases: pull requests go to `main`.

