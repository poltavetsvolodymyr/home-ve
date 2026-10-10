# Architecture

## Overview

```
browser on the LAN or through a VPN
   │  https://<host>
   ▼
nginx (on the host)
   ├── /          → files from /var/www/home          (frontend, built by Vite)
   └── /api/      → http://127.0.0.1:5000             (backend, Kestrel; console over WebSocket)
                        │
                        ├── /etc/vm/*.conf               VM settings: reads and writes
                        ├── systemctl show/start/stop    VM state and actions, via polkit
                        ├── /run/vm-<name>/vnc.sock      VM screen for the console
                        ├── /proc, /sys                  load of the host and of each VM, temperature, bridges
                        └── journalctl -o json           log of vm@<name>.service

vm@<name>.service (root)
   └── /usr/local/sbin/vm-run <name>   reads /etc/vm/<name>.conf, validates it, creates the tap "cables"
          └── exec qemu-system-x86_64  sockets in /run/vm-<name>/: qmp, qga, serial, vnc
   └── on stop: vm-stop (power button, then the plug), then the taps are removed
```

- **Frontend**: a static React application. It gets data only from `/api/...` and re-fetches it every few
  seconds (`usePoll`). The console is noVNC over WebSocket.
- **Backend**: a single self-contained binary `deploy/app/home-backend` (.NET included). It serves only JSON
  (and the console WebSocket), listens only on loopback, runs as the user `home-backend` without root,
  in a systemd sandbox.
- **VMs are started by systemd, not by the backend**: `vm@<name>.service` → `vm-run` → QEMU. The backend only asks
  systemd (start/stop/restart/kill) and edits the `.conf`. If the backend crashes or is stopped, the VMs do not notice.
- **The build** runs on the developer's computer (`build.ps1`), and the result is committed to `deploy/`.
  The host builds nothing: `update.sh` fetches the latest version of its channel (stable or dev) and installs the files.

## Permissions: who can do what

The backend has to manage VMs without being root. So permissions are granted narrowly:

| What | How it is allowed | What is not allowed |
|---|---|---|
| start / stop / restart / kill a VM | polkit, `deploy/vm/50-home-backend.rules`: only `vm@<name>.service` and only these four actions | any other units, enable/disable, editing units |
| delete a VM together with its disk | the same polkit rule: only `start` for `vm-disk-remove@<name>.service`. The script runs as root and removes the volume `<group>/<name>`, only a thin one in the pool `data` and only for a stopped VM | deleting any other volume |
| backups | the same polkit rule: `restart` for `vm-backup@<name>.service`, `start` for `vm-restore@<name>:<time>` and `vm-backup-delete@<name>:<time>`. The scripts run as root and validate the name, the time and the disk themselves: only the thin `<group>/<name>` of that very VM; restore only onto a stopped VM | reading the backups themselves: the folders `/var/backups/vm/<name>` give the group `home-backend` only the list of files, the files themselves are 0600 root |
| offsite upload | the same polkit rule: `restart` for `vm-offsite.service`. `/etc/vm-offsite` is `root`, 0711: the backend can see whether `rclone.conf` exists, but cannot read it | R2 keys and encryption passwords: root only |
| Update button | the same polkit rule: only `restart` for `home-update.service`. The unit runs `update.sh` as root | choosing what to run: the command is fixed in the unit, the backend can only restart the unit |
| ISO images | the folder `/var/lib/home-backend/iso` belongs to the backend; it downloads files there from a URL itself | slipping a host file to a VM: `vm-run` opens the ISO itself and checks the opened file (see below) |
| VM settings | `/etc/vm` is owned by `root:home-backend` with mode 0775, the unit has `ReadWritePaths=/etc/vm` | writing anywhere else: `ProtectSystem=strict` |
| console | after the VM starts, `vnc.sock` gets the group `home-backend` and mode 0660 (`ExecStartPost` in `vm@.service`) | `qmp.sock`, `qga.sock`, `console.sock`: 0600, root only. QMP can do almost anything, up to reading host files |
| VM log | the group `systemd-journal` | — |

**The `.conf` is written by the unprivileged backend and read by root.** So `vm-run` does not execute the file
(no `source`); it parses it line by line and checks every value by the same rules as the backend
(`VmConfigFile.cs`): name, number of cores and memory, MAC and bridge format, no more than 8 cards. And the disk must be
a thin LVM volume in the pool `data`. This way even a compromised backend cannot give a VM the host's root partition.
The backend does not touch the disk path when settings change. A new VM's disk is always `/dev/<group>/<name>`, and it
is created not by the backend but by `vm-run` on first start, and only a volume named after the VM (`DISK_SIZE` GiB, thin, in the pool).

**ISOs live in the backend's folder and are read by root.** So `vm-run` does not pass a path to QEMU; it opens the file
itself (descriptor 3, QEMU reads it via `/proc/self/fd/3`) and checks the already opened file: a regular file, located
directly in `/var/lib/home-backend/iso`, owned by `home-backend`. A symlink to `/dev/home/root`, a hard link to a file
owned by root, or swapping the file between the check and the open do not get through.

## The `/etc/vm/<name>.conf` format

```
# VM "router", run by vm@router.service. After editing: systemctl restart vm@router
CPUS=2
MEMORY=2048
DISK=/dev/home/router
NET=br-lan BC:24:11:14:4D:CD
NET=br-wan BC:24:11:C7:E4:7B
AUTOSTART=yes
```

| Key | Value | Rule |
|---|---|---|
| `CPUS` | number of virtual cores | 1–64 |
| `MEMORY` | memory, MiB | 128–262144 |
| `DISK` | `/dev/<group>/<volume>` | thin volume in the pool `data` (checked by `vm-run`) |
| `NET` | `<bridge> <MAC>`, one line per card, in slot order | the bridge exists, the MAC is not multicast and not repeated, up to 8 cards |
| `AUTOSTART` | `yes` / `no` | `vm-autostart.service` starts VMs with `yes` at boot |
| `DISK_SIZE` | GiB, optional | if the volume does not exist yet, `vm-run` creates it with this size (only a volume named after the VM) |
| `CDROM` | `.iso` file name, optional | an ISO from `/var/lib/home-backend/iso` in the CD drive; boots from it if the disk is empty |
| `BACKUP` | `no`, optional | without this line the VM is backed up every night (`vm-backup-all`); with `no` it is skipped |

Empty lines and lines with `#` are skipped. The backend rewrites the whole file (temporary file + rename),
so your own comments in it do not survive.

Hardware changes apply on the next VM start, as in Proxmox. Card N inside the guest is slot N:
as long as the MAC is the same, the guest sees "the same" card (the router's `.link` files bind the names `ens18`/`ens19` to the MAC).

## Backend: `backend/HomeBackend`

```
Program.cs                 CLI command (set-password) or web server
Hosting/
  HomeBackendServices.cs   AddHomeBackend(): config + registration of all features
  HomeBackendPipeline.cs   UseHomeBackend(): middleware in order + all endpoints
Configuration/
  HomeBackendOptions.cs    the HomeBackend section of /etc/home-backend/config.json, default values
Security/
  PasswordHash.cs          PBKDF2 password hash
  NetworkAllowlist.cs      requests not from AllowedNetworks are dropped without a response
  SecurityHeaders.cs       nosniff, DENY, no-referrer, CSP
Api/
  ApiJsonContext.cs        list of all types the API (de)serializes
  ErrorResponse.cs         body of a 400 response: {"error": "..."}
  HostCommandFailure.cs    systemctl/journalctl failure → 502 Bad Gateway
Features/                  see below
Infrastructure/
  ProcessRunner.cs         runs programs without a shell, with a timeout
  LinuxFiles.cs            reads single-line files in /proc and /sys
  DataSources.cs           AddDataSource<>(): Linux implementation or mock
  MockClock.cs             shared fake boot time for the mocks
Cli/                       home-backend hash-password / set-password
```

### Features

| Feature | Page | Endpoints | Data source (Linux / mock) |
|---|---|---|---|
| `Auth` | login | `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me` | hash from the config |
| `Host` | Host | `GET /api/host` | assembled from SystemStatus |
| `SystemStatus` | (on Host) | — | `LinuxSystemSource`: /proc, /etc; `CpuMonitor`; `CpuTemperatureMonitor` + `HwmonCpuTemperatureSource` (k10temp/coretemp from /sys/class/hwmon) |
| `Vms` | VMs, VM page, New VM | `POST /api/vms` (create), `DELETE /api/vms/{name}[?disk=true]` (delete), `GET /api/vms`, `GET /api/vms/{name}`, `POST /api/vms/{name}/{start\|shutdown\|reboot\|poweroff}`, `PUT /api/vms/{name}/config`, `GET /api/vms/{name}/logs`, `GET /api/vms/{name}/console` (WebSocket), `GET /api/bridges` | `LinuxVmHost`: /etc/vm, systemctl, /proc/&lt;pid&gt;, /sys/class/net; `MockVmHost` |
| `Isos` | ISO images | `GET /api/isos`, `POST /api/isos` (download from a URL), `DELETE /api/isos/{name}` (delete or cancel a download) | `IsoStore`: the `IsoDir` folder, background downloads via `.<name>.part` |
| `Backups` | Backups tab of a VM | `GET/POST /api/vms/{name}/backups` (list / Back up now), `POST /api/vms/{name}/backups/{time}/restore`, `DELETE /api/vms/{name}/backups/{time}` | `LinuxBackupHost`: list of files in `BackupDir`, `systemctl` for the units `vm-backup@`/`vm-restore@`/`vm-backup-delete@` and their log; `BackupRestores` keeps a running restore in memory; `MockBackupHost` |
| `Offsite` | Settings | `GET /api/host/offsite`, `POST /api/host/offsite` | `SystemctlOffsiteRunner`: `systemctl restart/show vm-offsite.service`, its log, whether `/etc/vm-offsite/rclone.conf` exists; `MockOffsiteRunner` |
| `Logs` | (Logs tab of a VM) | — | `JournalctlSource`: `journalctl -o json -u vm@<name>.service` |
| `Update` | Settings (gear in the header) | `GET /api/host/update`, `POST /api/host/update` | `SystemctlUpdateRunner`: `systemctl restart/show home-update.service` + its log; `MockUpdateRunner` |

Inside `Vms`:

- **`VmConfigFile`**: the `.conf` format: parsing, writing, validation. The same rules as in `vm-run`.
- **`SystemctlVmUnits`**: which `systemctl` commands are behind which action, and parsing of `systemctl show`:

  | Action | Command | What happens |
  |---|---|---|
  | Start | `start vm@<name>` | start |
  | Shut down | `stop --no-block` | `ExecStop` (`vm-stop`) "presses the power button" via QMP and waits 120 s for the guest. Whatever ignores the button (an installer, a boot loader menu, a hung system) gets SIGTERM, i.e. the power cord is pulled, and the VM still ends up Stopped, not Failed |
  | Reboot | `restart --no-block` | the same shutdown, then a start. This is how new settings are picked up |
  | Power off | `kill --signal=TERM`, then `stop --no-block` | pull the power cord: on SIGTERM QEMU drops the guest immediately and exits with code 0, so the VM becomes Stopped, not Failed. `stop` is a fallback in case QEMU does not react |

- **`VmMonitor`** (`BackgroundService`) re-reads the configs, the unit states and, for running VMs,
  `/proc/<pid>/stat` and `statm` of the QEMU process every 2 seconds. CPU % is relative to the VM's own cores
  (100 % = all of its vCPUs are busy), memory is the RSS of the QEMU process. Endpoints return the latest snapshot,
  and after an action or a save the backend refreshes it immediately.
- **`VmConsole`**: a bridge WebSocket ⇄ unix socket `vnc.sock`. VNC bytes are passed as is (subprotocol `binary`);
  the protocol is parsed by noVNC in the browser. VM not running → 404, no socket → 502. Each direction
  closes on its own: if the VM closes, the browser gets a normal close; if the browser closes, the socket to the VM is closed.

### Background measurements

CPU usage is a rate, so it needs two measurements with a pause between them. That is why `CpuMonitor` and `VmMonitor` read
the counters every 2 seconds, and `CpuTemperatureMonitor` every 5 seconds. Endpoints just return the latest value.

### Request path

The order is set in `Hosting/HomeBackendPipeline.cs`:

1. **ForwardedHeaders**: takes the client IP from `X-Forwarded-For`, which nginx sets. The header is trusted
   only from loopback.
2. **NetworkAllowlist**: if the IP is not in `AllowedNetworks` (LAN and VPN), the connection is dropped without a response.
3. **SecurityHeaders**.
4. **RateLimiter**: for login: no more than 10 attempts per 5 minutes from one IP.
5. **Authentication**: cookie `home-backend`, lives 7 days, extended on use.
6. **Authorization**: everything under `/api` except login and logout requires a login. Without a session the response is 401.
7. **WebSockets**: for the console; the same cookie, the same allowlist.
8. **Endpoint**. If systemctl or journalctl fails, the response is 502 with the error text.

### Trimming: what not to do

The binary is published trimmed (`Properties/PublishProfiles/Home.pubxml`), so there must be no reflection:

- JSON only through source generation: a new request or response type must be added to `Api/ApiJsonContext.cs`;
- endpoints are built by the Request Delegate Generator, the config by the binding generator;
- the trimming analyzers are enabled in the normal build, so `dotnet build` shows a warning right away.
  The build must have no warnings.

A minimal API pitfall: a handler method whose only parameter is `HttpContext` matches the
`RequestDelegate` overload, and its result is silently discarded (this would have broken logout). In such cases
use a lambda (see `AuthFeature`); the compiler warns about this (ASP0016).

## Frontend: `frontend/src`

```
main.tsx                  theme before the first render, global styles, <App/>
app/
  App.tsx                 login or the router
  routes.tsx              tabs: VMs and Host (path, title, icon, what to render)
  router.tsx              react-router: tabs + /vms/:name inside Layout, unknown path → /vms
  Layout.tsx              header + page of the current path (<Outlet/>) + bottom tabs
  AppHeader.tsx           header: brand, tabs (on a wide screen), link to the router, theme, logout
  NavTabs.tsx, ThemeToggle.tsx, theme.ts
features/
  auth/                   LoginPage, useAuthState, api.ts
  settings/               SettingsPage = UpdateCard: Update button with confirmation, status and update.sh output
  host/                   HostPage = HostStats (CPU, temperature, memory, disk, uptime) + HostCard
  isos/                   IsosPage: download an ISO from a URL, download progress, list and deletion
  vms/
    VmsPage.tsx           New VM and ISO images buttons, VM cards with status and load
    NewVmPage.tsx         new VM (/vms/new): name, cores, memory, disk size, ISO, cards; "start and open console"
    VmDelete.tsx          deleting a stopped VM; with the disk, only after typing its name
    NetsEditor.tsx, CdromField.tsx   shared parts of the new VM and settings forms
    VmPage.tsx            one VM: action buttons + tabs ?tab=summary|console|settings|logs
    VmActions.tsx         Start / Shut down / Reboot / Power off, with confirmation for the disruptive ones
    VmSummary.tsx, VmMeters.tsx
    VmSettingsForm.tsx    cores, memory, network cards, autostart; after saving offers a reboot
    VmConsole.tsx         noVNC (loaded only when the tab is opened): scaling, Ctrl+Alt+Del, full screen,
                          input line for phones (letters are sent as key presses)
    VmKeys.tsx            key panel under the screen: Esc, Tab, arrows (repeat while held), Home/End,
                          PgUp/PgDn, F1–F12, sticky Ctrl/Alt/Shift for the next key or line
    VmLogs.tsx            log of vm@<name>.service
    settings.ts, vmState.ts, keysyms.ts, logLevel.ts   pure functions with tests
shared/                   HTTP client, usePoll, session, formatting, UI kit
styles/                   tokens.css (theme colors), base.css, index.css
```

Rules:

- **Page = folder in `features/`**: the page component, `api.ts` (response types + request functions),
  its own components, pure functions with tests (`*.test.ts` next to them), its CSS.
- **`shared/` knows nothing about features**, features know nothing about `app/`. A feature may use the types and
  requests of another feature, but not its components.
- Imports go through the `@/` alias: `@/shared/ui`, not `../../shared/ui`.
- **Routing**: react-router with real paths (`/vms`, `/vms/router?tab=console`, `/host`). There is one file,
  `index.html`: nginx serves it for any path that does not exist on disk (`try_files`, see deployment.md),
  and the router picks the page by the path. The header is not re-rendered on navigation, only the page changes.
- **Data** only through `usePoll`. It logs out by itself on 401, and returns the error and the latest data to the page.
  When you come back to a tab, the page immediately shows the previous data and refreshes it right away.
- **Skeleton first.** A page renders all of its markup from the first frame, and values that have not arrived yet
  are shown as `<Skeleton/>` (gray bars; `<SkeletonRows/>` for tables). When the data arrives, the bars are replaced
  by values and the layout does not jump. No "Loading…" instead of a page.
- **CSS**: plain global classes. Global styles (`styles/index.css`: tokens → base → UI kit)
  load first; feature styles are imported by their components and come after. So, for example, `.login-card`
  can override the `padding` of `.card`. Colors only through variables from `tokens.css`; then the dark
  theme works by itself.
- **Theme**: the `<html data-theme>` attribute, dark by default, the choice is stored in `localStorage`.

## Recipes

### Add a VM action

1. A value in `enum VmAction` (`Features/Vms/VmState.cs`) and the commands in `SystemctlVmUnits.ActionCommands`.
2. If a new systemctl verb is needed, add it to `deploy/vm/50-home-backend.rules`, otherwise polkit refuses.
3. Frontend: `features/vms/vmState.ts` (`availableActions`, `actionLabels`, `confirmText`) and the test next to it.

### Add a key to `.conf`

Three places must match: `VmConfigFile.cs` (parsing, writing, validation + test), `deploy/vm/vm-run`
(parsing and validation, otherwise the VM does not start, with "unknown key") and the table above. If the key should be
editable from the web UI, also `VmSettingsRequest` in `VmsFeature.cs` and the form `VmSettingsForm.tsx`.

### Add a backend setting

A property in `Configuration/HomeBackendOptions.cs`; default lists are set in `WithDefaults()`
(arrays from different config sources are merged by index). Then `deploy/config.example.json` and the table
in [deployment.md](deployment.md).
