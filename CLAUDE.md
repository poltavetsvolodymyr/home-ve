# CLAUDE.md

Notes for Claude Code sessions in this repository. The docs have the details: [README.md](README.md),
[docs/architecture.md](docs/architecture.md), [docs/deployment.md](docs/deployment.md),
[docs/development.md](docs/development.md).

## What this is

home-ve: a single-host VM manager. Debian 13 + QEMU/KVM on LVM thin volumes, a web UI made for phones.

- `backend/HomeBackend/` — ASP.NET Core 10, minimal API, one folder per feature under `Features/`
  (Auth, Backups, Host, Isos, Logs, Offsite, SystemStatus, Update, Vms). `Installer/` is the installer image's
  page (`home-backend installer`). Runs without root, listens on 127.0.0.1 behind nginx.
- `frontend/` — React + Vite + TypeScript. `src/features/<page>`, `src/shared/` (http, usePoll, format, UI kit),
  `src/installer/` (a second Vite entry, `installer.html`).
- `deploy/` — everything a host has in `/opt/home-ve` (a sparse checkout of only this folder): `install.sh`
  (install and every update, idempotent), `update.sh`, `fetch-build.sh` (downloads the commit's build from
  GitHub releases), `vm/` (vm-run and the root helpers behind systemd units), `nginx/`, `offsite/`, `preseed/`.
- `installer/` — the live installer image (live-build): `build-live.sh`, `live/` (packages, hooks, files, boot menus).
- `.github/` — `ci.yml` (checks, the dev build of every commit, preseed ISO), `installer.yml` (live ISO + its
  end-to-end QEMU test, only when what installs a host changes), `release.yml` (release build, both ISOs, the
  installer tested on the stable channel before it is attached), `test-installer.sh`.

## Checks before every push

```bash
dotnet build backend/HomeBackend.slnx --nologo -warnaserror
dotnet test --solution backend/HomeBackend.slnx
dotnet publish backend/HomeBackend -p:PublishProfile=Home -p:PublishDir=/tmp/pub/ --nologo -warnaserror
cd frontend && npm ci && npm run check      # tsc, eslint, prettier --check, vitest
bash -n <script> && shellcheck <script>     # every shell script touched
```

`bash -n` matters: shellcheck does not catch a function named after a reserved word (`in`).

## Conventions

- Code, comments, docs and commit messages in English. Comments say why, not what. Match the surrounding style.
- Buttons with an icon: just `<button><Icon size={15} aria-hidden /> Label</button>`; the global `button` rule lines
  them up (docs/architecture.md, "Buttons with an icon"). Look at a new button on a 390px-wide mock screenshot.
- No new dependencies without a good reason.
- Shell: `set -euo pipefail`; never `pkill -f` (it matches the calling shell); `rm` with `"${VAR:?}"`.
- Security boundaries: the backend never takes a path from a request (a VM's disk is always
  `/dev/<group>/<name>`); root scripts (`vm-run` and friends) check their input again and execute nothing from
  a config.
- The web UI password lives only in `/var/lib/home-backend/password` (PBKDF2), never in the config.
- Commits: author is the maintainer; end the message with `Co-Authored-By: Claude <noreply@anthropic.com>`.
  No model identifiers in commits, code or docs. History is not rewritten.

## Branches and releases

- `main` — every commit; hosts on the dev channel. The maintainer may push to `main` directly.
- `stable` — releases; hosts on the stable channel. Only maintainers move it, by fast-forwarding to a `main`
  commit after CI's checks pass locally. Contributors never target `stable`, not even with a pull request.
- Releases (`vX.Y.Z`, target `stable`) are created by the maintainer in GitHub's UI; `release.yml` attaches the
  builds and ISOs.

## Testing the installer

- CI (`installer.yml`) installs the image in QEMU with KVM, BIOS and UEFI, through the page's API, boots the disk
  and signs in with the password set in the installer. Its log and screenshots go to the `dev` pre-release as
  `home-ve-installer-test-<bios|uefi>.log` / `.png`; on a failure the log has the installed system's journal.
- A push to `main` that touches the installer's paths cancels the running installer workflow (concurrency).
- Locally: `dotnet run --project backend/HomeBackend --no-launch-profile -- installer --Installer:Mock=true
  --Installer:Urls:0=http://localhost:5081`, then `npm run dev` and `http://localhost:5173/installer.html`.

## Working with the maintainer

- Talk to the maintainer informally: in Russian, «ты», never «вы».
- A question is a question: answer it, change nothing unless asked.
- Explain configs and commands line by line.
- Never ask for or handle tokens or keys.
