# Deployment

The host keeps a sparse checkout of the repository in `/opt/home-ve`. It contains only the `deploy/` folder:

```
deploy/
  app/home-backend        backend: a single self-contained linux-x64 binary (downloaded, not in git)
  www/                    built frontend; install.sh copies it to /var/www/home (downloaded, not in git)
  fetch-build.sh          downloads the build of the checked-out commit from the GitHub releases
  home-backend.service    systemd unit for the backend
  config.example.json     template for /etc/home-backend/config.json
  vm/                     vm-run, qmp, vm@.service, vm-autostart(.service), polkit rule
  nginx/home.conf         nginx site (install.sh installs it itself)
  install.sh              install and every update (idempotent)
  update.sh               latest version of the channel + install.sh
  preseed/                answers for the Debian installer: the automated installation below
```

There are two ways to get there: the **automated installation**, which installs Debian and home-ve in one go on an
empty machine, or **by hand** on a Debian that is already installed ([Preparing the host](#preparing-the-host) and
the sections after it).

## Automated installation

The Debian installer can take its answers from a file (a "preseed"). `deploy/preseed/preseed.cfg` sets up the
machine the way the rest of this page does by hand: LVM with room for the backups and the VMs, the bridge, home-ve.

**What you need:** a machine (or a VM) with a wired network card and DHCP on the network, internet access, and a
disk of at least **64 GB that will be erased whole**.

1. **The installer image**: `home-ve-vX.Y.Z-installer.iso` from the latest
   [release](https://github.com/poltavetsvolodymyr/home-ve/releases/latest) (about 70 MB; the dev channel's is
   `home-ve-installer-dev.iso` on the pre-release **dev**). It is Debian's own netboot installer, kernel and boot
   loader untouched, with the answers below inside; it downloads Debian while it installs. Write it to a USB stick
   (`dd`, balenaEtcher, Rufus in DD mode), or put it into a VM's CD drive.
2. **Boot it and choose Install** (or Graphical install). That's all the image needs.

   Or, with any Debian 13 image instead (netinst from [debian.org](https://www.debian.org/distrib/)): **Advanced
   options → Automated install**, then, when it asks for the preconfiguration file, enter
   `https://raw.githubusercontent.com/poltavetsvolodymyr/home-ve/stable/deploy/preseed/preseed.cfg`
   (`main` instead of `stable` for the dev channel).
3. It asks only for: **the disk** (with a single disk it takes that one), a confirmation that names that disk
   (everything on it goes), **the root password**, and **your own user** (name and password), whom you log in as
   over SSH before `su -`.
4. It installs, reboots, and on that first boot `home-ve-firstboot.service` finishes the job; its progress shows on
   the screen. When it is done, the screen shows the web UI's address and the setup code, then the login prompt.
   Open the address and set the password with that code (later, as root: `cat /var/lib/home-backend/setup-code`).

What the answers set up:

| | |
|---|---|
| language, keyboard, time zone | English, US, Europe/Berlin (`dpkg-reconfigure locales`, `keyboard-configuration`, `tzdata` to change them) |
| host name | `home` |
| disk | `/boot`, then the LVM volume group `home`: `root` 30 GB, `swap` 2 GB; on the first boot `backups` (a quarter of the group, at most half of what is free), mounted at `/var/backups/vm`, and the thin pool `data` (90% of the rest; the remainder is a reserve, see [the pool](#the-data-thin-pool)) |
| software | the base system, SSH server, git, curl, thin-provisioning-tools; `install.sh` adds the rest |
| network | the bridge `br0` on the card the installer used, by DHCP: [Bridge for VM networking](#bridge-for-vm-networking), done for you. The router sees a new client, so the address after the reboot is usually not the installer's: read it on the screen |
| home-ve | the stable channel in `/opt/home-ve` (or the dev channel, from `main`), installed by `install.sh` on the first boot |

The installer's own log of the home-ve part is `/var/log/home-ve-install.log`; the first boot's is
`journalctl -u home-ve-firstboot`. If the first boot could not finish (no network, for example), it tries again at the
next boot; or run `/opt/home-ve/deploy/preseed/firstboot.sh` by hand, it skips whatever is done already.

Limits: one bridge on one card, by DHCP (with a static address the installer's network setup stays, without a bridge:
then follow [Bridge for VM networking](#bridge-for-vm-networking)); with several disks, GRUB goes onto the first
one, so pick that one for the system.

## Preparing the host

Run everything as root (`su -`), preferably over SSH from your own terminal, where copy and paste just work: tick
**SSH server** under "Software selection" when installing Debian (or later: `apt-get install -y openssh-server`), log
in as the regular user you created, then run `su -`. The one exception is the bridge step below: the network goes
down for a moment there, so do it from the host's keyboard or console.

### Requirements

- **Debian 13 (trixie)**, x86-64, connected to your home network.
- **CPU virtualization** enabled in the BIOS (Intel VT-x / AMD-V, sometimes called SVM Mode):
  ```bash
  grep -cwE 'svm|vmx' /proc/cpuinfo
  ```
  A number greater than 0 means it is available. 0 means you need to enable it in the BIOS. If the host is itself a
  virtual server, whoever runs it must enable nested virtualization.
- **Space for VM disks**: an LVM thin pool named `data` (see below). Easiest if you plan for it when installing
  Debian: choose **Guided - use entire disk and set up LVM**, and at **Amount of volume group to use for guided
  partitioning** give the system only part of it (for example `15 GB`); the rest becomes the pool. Or have a second,
  empty disk for the VMs. The web UI itself uses about 200 MB of memory;
  the rest is for the VMs.

### The `data` thin pool

VM disks are thin volumes in the `data` pool: space on the host is used only as the VM writes data.
`install.sh` finds the LVM volume group that contains this pool by itself.

```bash
apt-get install -y lvm2 thin-provisioning-tools
vgs
```

- `lvm2` is LVM itself. Without `thin-provisioning-tools` the thin pool will not come up after a reboot;
- `vgs` shows which LVM volume groups already exist and how much free space they have (`VFree`). No output, or
  `command not found` before installing, means there are no groups.

Then work out which case you are in. `lsblk` shows your disks and what is on them:

```bash
lsblk
```

> **Never use a disk or partition that has anything in the `MOUNTPOINTS` column (`/`, `/boot`, `[SWAP]`, …), or that
> has partitions under it.** That is your running system: the commands below would erase it.

| What you see | Case |
|---|---|
| `vgs` lists a group with free space (`VFree` of a few GB or more) | **A** |
| a second disk (e.g. `sdb`) with no partitions under it and no mount point | **B** |
| the system disk is bigger than the sum of its partitions (for example `sda 60G`, but `sda1` + `sda5` = 40G) | **C** |
| none of these: the system takes the whole disk, no LVM, no second disk | **D** |

For example, this is case **D** — `sda` is 40G and its partitions fill it; `sda2` (1K) is only a container for `sda5`,
and `sr0` is the CD drive:

```
NAME   MAJ:MIN RM  SIZE RO TYPE MOUNTPOINTS
sda      8:0    0   40G  0 disk
├─sda1   8:1    0 37.9G  0 part /
├─sda2   8:2    0    1K  0 part
└─sda5   8:5    0  2.1G  0 part [SWAP]
sr0     11:0    1  756M  1 rom
```

**A. A volume group exists and has free space** (Debian was installed with "use entire disk and set up LVM", and
space was left free at the "Amount of volume group to use" question; the group is named after the host, for example `debian-vg`):

```bash
lvcreate --type thin-pool -l 90%FREE -n data debian-vg
```

**B. A separate empty disk** (for example `/dev/sdb`: in `lsblk` it has no partitions under it and no mount point).
Everything on it will be erased. Check the name twice:

```bash
pvcreate /dev/sdb
vgcreate vms /dev/sdb
lvcreate --type thin-pool -l 90%FREE -n data vms
```

**C. Free space at the end of the system disk** (`lsblk`: the disk is larger than the sum of its partitions):

```bash
apt-get install -y fdisk
echo ',,8e' | sfdisk --append /dev/sda
partx -a /dev/sda
lsblk
```

- `sfdisk --append` adds a partition in the free space at the end (`,,` means from the start of the free space to the
  end of the disk, `8e` is the "Linux LVM" type). Existing partitions are not touched;
- the disk is in use (the system is on it), so `sfdisk` reports that the kernel did not re-read the table, and `partx -a`
  reports that it cannot add the old partitions. This is normal: `partx` adds only the new one;
- a new partition (for example `sda3`) now shows up in `lsblk`. If it does not, `reboot`.

Then continue as in option B, using this partition: `pvcreate /dev/sda3`, `vgcreate vms /dev/sda3`,
`lvcreate --type thin-pool -l 90%FREE -n data vms`.

**D. The system takes the whole disk.** There is no room for VM disks yet. Either add a second disk and use case B,
or reinstall Debian with LVM and leave room: in the installer's partitioning, choose **Guided - use entire disk and
set up LVM**, then **All files in one partition**, and at **Amount of volume group to use for guided partitioning**
enter a size for the system only (for example `15 GB`; Debian itself needs a few GB). The rest of the group stays
free, and after the installation you are in case A.

Check: `lvs` shows `data` with the attributes `twi-a-tz--` (thin pool, active).

Why `90%FREE` and not everything: the rest of the group is a reserve, so you can grow the pool's metadata
(`lvextend --poolmetadatasize`) or the pool itself if needed.

> **Backups on the same disk?** The backup volume (see [Backups](#backups)) is a separate, regular volume, and a thin
> pool cannot be shrunk later. So if the backups are to live in this volume group too, create the backup volume
> **before** the pool, then the pool takes 90% of what is left.

If you already ran `install.sh` before the pool existed, run it again: it will add the group to the settings.

### Bridge for VM networking

A VM's network card connects to a bridge on the host. A bridge is a virtual switch that both the host's real network
card and the VMs' cards are plugged into. That way a VM is a regular device on your home network, like any other, and
gets its address from the router. When you create a VM in the web UI, you choose a bridge; until a bridge exists, VMs have no network.

This is done with systemd-networkd: it is already part of Debian, so there is nothing to install. **Do this from the host's
keyboard (or from the console, if the host is itself virtual), not over SSH**: the network goes down during the switch.
A Wi-Fi card cannot be added to a bridge; you need a wired one.

The card's name:

```bash
ip -br link
```

You need the one that is `UP` and is not `lo`, for example `enp1s0` or `eno1`. Put its name into `NIC` (this is
the only thing you change; every command below takes the name from there) and check it:

```bash
NIC=enp1s0
ip link show "$NIC"
```

The second line must print the card, not `Device "…" does not exist`. Then, in the same shell:

```bash
printf '[NetDev]\nName=br0\nKind=bridge\n' > /etc/systemd/network/10-br0.netdev
printf '[Match]\nName=br0\n\n[Network]\nDHCP=ipv4\n\n[DHCPv4]\nClientIdentifier=mac\n' > /etc/systemd/network/10-br0.network
printf '[Match]\nName=%s\n\n[Network]\nBridge=br0\n' "$NIC" > /etc/systemd/network/20-br0-port.network
cat /etc/systemd/network/20-br0-port.network
```

- `10-br0.netdev` creates the bridge `br0`;
- `10-br0.network`: the bridge gets the address via DHCP. `ClientIdentifier=mac` makes it identify itself to the router
  by MAC, so the address does not change;
- `20-br0-port.network`: the card is added to the bridge and no longer gets an address of its own. `%s` is replaced
  with the name in `NIC`; `cat` shows the result: `Name=` must be your card.

Now remove the card from the old network configuration (ifupdown) and enable networkd.

> **After this reboot the host will most likely have a different IP address** (the router sees a new DHCP client, see
> below). SSH and the web UI at the old address stop working. Find the new address on the host's own screen
> (`ip -br a`, the line for `br0`) or in your router's list of devices (look for the host's name), and connect to that.


```bash
cp /etc/network/interfaces /etc/network/interfaces.bak
sed -i -E "s/^(allow-hotplug|auto) $NIC\$/# &/; s/^iface $NIC .*/# &/" /etc/network/interfaces
grep "$NIC" /etc/network/interfaces
systemctl enable systemd-networkd
reboot
```

- `sed` comments out the lines about this card in `/etc/network/interfaces`; otherwise two services would manage it.
  `grep` shows them: every line must now start with `#`;
- `/etc/resolv.conf` (DNS) stays as the old DHCP client left it: it is already correct. If the DNS server on your
  network changes later, edit this file or install `systemd-resolved`.

After the reboot:

```bash
networkctl                  # br0: routable configured, your card: enslaved configured
ip -br a                    # the address is now on br0
getent hosts debian.org     # DNS works
```

**The host's address usually changes once**: the router sees a new DHCP client. The new address is in `ip -br a`. To keep
the web UI's address from changing again, reserve it for the host in your router's DHCP settings.

**Only if something went wrong** (no network after the reboot) — undo it all from the host's keyboard:

```bash
rm /etc/systemd/network/10-br0.* /etc/systemd/network/20-br0-port.network
cp /etc/network/interfaces.bak /etc/network/interfaces
reboot
```

For two networks (for example, a host for a router VM: LAN and WAN), create two bridges the same way, `br-lan` and
`br-wan`, each with its own card. The host needs an address on only one of them; in the second bridge's `.network`,
write `LinkLocalAddressing=no` instead of `DHCP=`.

## First installation

The host is prepared (see the section above). Run everything on the host as root.

### 1. Clone only `deploy/`

```bash
apt-get install -y git
git clone --filter=blob:none --sparse --branch stable https://github.com/poltavetsvolodymyr/home-ve.git /opt/home-ve
cd /opt/home-ve && git sparse-checkout set deploy
```

- `--filter=blob:none --sparse` downloads the history without file contents, and `sparse-checkout set deploy` then
  fetches only the `deploy/` folder: the scripts. `install.sh` downloads the backend and frontend built for this
  version by CI. The host needs no source code and no build tools;
- `--branch stable`: the latest release. Updates later come from the same place (Update now in the web UI, or
  `deploy/update.sh`), see [Updating](#updating) for the dev channel.

### 2. Install

```bash
bash /opt/home-ve/deploy/install.sh
```

.NET is not installed on the host. What the script does:

- installs whatever is missing from `qemu-system-x86`, `socat`, `lvm2`, `thin-provisioning-tools`, `dbus`, `polkitd`, `zstd`, `curl`, `ca-certificates`,
  `nginx`, `openssl`. `systemctl` run by a regular user talks to systemd over D-Bus, and a minimal Debian
  may not have it;
- creates the system user `home-backend` (no shell, no home directory);
- creates `/etc/home-backend/config.json` from the template (mode 0640);
- **finds where to put VM disks**: the LVM volume group that contains the `data` thin pool, and writes it to `DiskGroup`
  (only if that is still empty). If there is no pool, the script tells you how to create one, for example
  `lvcreate --type thin-pool -l 90%FREE -n data <group>`; until the pool exists and you run `install.sh` again,
  the web UI does not create VMs;
- **converts old `.conf` files** to the new format: `NETS="br-lan=… br-wan=…"` → lines `NET=br-lan …`,
  and if `vm@<name>` was enabled (`enable`), writes `AUTOSTART=yes` and disables that `enable`. VMs are now started
  at boot by `vm-autostart.service`. The old file is kept next to it as `<name>.conf.bak`;
- installs `vm-run`, `qmp`, `vm-autostart` to `/usr/local/sbin`, the units to `/etc/systemd/system`, the polkit rule
  to `/etc/polkit-1/rules.d`; `/etc/vm` gets the group `home-backend` and mode 0775;
- installs the backend unit, enables it and restarts it;
- replaces `/var/www/home` entirely with the fresh frontend;
- **nginx**: the site `/etc/nginx/sites-available/home` is taken from `deploy/nginx/home.conf` on every run
  (the previous one is kept next to it as `home.bak`, and if `nginx -t` rejects the new one, the old one is restored). Which
  certificate to use is set in `/etc/nginx/home-ve/tls.conf`: the script creates it once and never touches it again.
  At first it points to a self-signed certificate for the host's name and addresses (`/etc/nginx/home-ve/selfsigned.*`, valid for 825 days;
  30 days before it expires the script makes a new one). If the site was already configured by hand, `tls.conf` gets its certificate.

Done: the web UI is at `https://<host address>/`. **The first visit sets the password**: the page asks for the setup
code that `install.sh` printed at the end (8 characters, like `ABCD-EFGH`; again any time with
`cat /var/lib/home-backend/setup-code`) and the password you choose, and signs you in. The code is there so that
nobody else on your network can set the password before you; it is gone once the password is set, and until then
the web UI shows nothing but this page. The password is kept as a hash in `/var/lib/home-backend/password` (see
Settings below for changing it). With the self-signed certificate the browser warns you once
(the connection is still encrypted). To get rid of the warning, use your own domain and certificate, step 3.

The script does not touch running VMs. A VM gets the new `vm-run` (and with it the browser console) on its next
restart. If the VM is a router, this means ~30 seconds without internet, so pick a convenient moment:

```bash
systemctl restart vm@router
ls -l /run/vm-router/      # vnc.sock: srw-rw---- root home-backend; other sockets root only
```

### 3. Your own domain and certificate (optional)

This is how the author does it: the domain is on Cloudflare, the certificate is from Let's Encrypt via acme.sh with
DNS validation, so the host does not need to be reachable from the internet. Below, replace `example.com`, `home.example.com` and `192.168.1.2` with your own values.

**Name.** Cloudflare → domain → DNS → Add record:

| Type | Name | IPv4 | Proxy |
|---|---|---|---|
| A | `home` | `192.168.1.2` | DNS only (grey cloud) |

From outside, there is nothing at this address (it is an address on your home network), but at home and over VPN it points to the host. If
your router's DNS server is protected against DNS rebinding (dnsmasq with `stop-dns-rebind`), allow your domain:
`rebind-domain-ok=/example.com/`. Check from the host: `getent hosts home.example.com` → `192.168.1.2`.

**Certificate.** You need a Cloudflare API token with the Zone → DNS → Edit permission for this domain, and the Zone ID
(on the domain's page, bottom right).

```bash
git clone --depth 1 https://github.com/acmesh-official/acme.sh.git /tmp/acme.sh
cd /tmp/acme.sh
./acme.sh --install --nocron --noprofile --home /opt/acme.sh --config-home /etc/acme.sh --accountemail you@email
cd / && rm -rf /tmp/acme.sh
chmod 700 /etc/acme.sh
read -rsp 'CF token: ' CF_TOKEN; echo
read -rp 'CF zone id: ' CF_ZONE
CF_Token=$CF_TOKEN CF_Zone_ID=$CF_ZONE /opt/acme.sh/acme.sh --config-home /etc/acme.sh \
  --issue --server letsencrypt --dns dns_cf -d example.com -d '*.example.com'
unset CF_TOKEN CF_ZONE
```

- `read -s` does not echo the input, and what you paste into `read` does not end up in the shell history.
- Paste only the value, without `CF_TOKEN=` and without quotes.
- acme.sh stores the token in `/etc/acme.sh/account.conf` (root only) for renewals.

Install the certificate for nginx, tell nginx to use it, and check for renewal once a day:

```bash
install -d -m 700 /etc/nginx/tls
/opt/acme.sh/acme.sh --config-home /etc/acme.sh --install-cert -d example.com \
  --key-file /etc/nginx/tls/example.com.key \
  --fullchain-file /etc/nginx/tls/example.com.crt \
  --reloadcmd 'systemctl reload nginx'
cat > /etc/nginx/home-ve/tls.conf <<'EOF'
ssl_certificate     /etc/nginx/tls/example.com.crt;
ssl_certificate_key /etc/nginx/tls/example.com.key;
EOF
nginx -t && systemctl reload nginx
cat > /etc/systemd/system/acme-renew.service <<'EOF'
[Unit]
Description=Renew certificates with acme.sh when due
After=network-online.target

[Service]
Type=oneshot
ExecStart=/opt/acme.sh/acme.sh --cron --config-home /etc/acme.sh
EOF
cat > /etc/systemd/system/acme-renew.timer <<'EOF'
[Unit]
Description=Daily certificate renewal check

[Timer]
OnCalendar=daily
RandomizedDelaySec=1h
Persistent=true

[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload && systemctl enable --now acme-renew.timer
```

`--cron` reads where to put the new certificate and what to reload from `/etc/acme.sh/example.com_ecc/example.com.conf`
(`Le_RealKeyPath`, `Le_RealFullChainPath`, `Le_ReloadCmd`): `--install-cert` wrote them there.

Open `https://home.example.com`.

### What is in `home.conf`

- `map $http_upgrade $connection_upgrade` and `location ~ ^/api/vms/[^/]+/console$`: the console works
  over WebSocket. By default nginx talks to the backend over HTTP/1.0 and drops the `Upgrade`/`Connection`
  headers; here it passes them through, so the connection is "upgraded" to WebSocket.
- `proxy_read_timeout 1h`: without it nginx closes the console after 60 seconds of no activity on the screen.
- A regex `location ~` takes priority over the plain `/api/` prefix, so the console requests go to it.
- `location /` with `try_files $uri $uri/ /index.html`: a file if there is one, otherwise the app's page, so an address
  like `/vms/router` opens the app (it is a single-page app: the browser, not nginx, knows its addresses).
- `location = /index.html` with `Cache-Control: no-cache`: every address of the app ends up here (through the
  `try_files` above). `no-cache` doesn't mean "don't store it": the browser asks every time whether the page changed
  (a cheap `304 Not Modified` when it didn't). So after an update it gets the new page, which names the new build's
  files, and not a stored old one that names files that are gone.
- `location /assets/` with `try_files $uri =404` and `Cache-Control: public, max-age=31536000, immutable`: the build's
  JS and CSS have a hash of their content in the name (`index-yng_cktJ.js`), so a name never gets other content and
  the browser may keep them for a year without asking. A file that is gone (a page opened before an update asks for
  the old build's) is a 404, not the page: the app then reloads once and gets the new build (`main.tsx`). Without
  this location the browser got the page's HTML as a script, and "'text/html' is not a valid JavaScript MIME type".
- An `add_header` in a `location` hides every `add_header` of the `server` block, `tls.conf` included. There are none
  now; if you add one to `tls.conf` (HSTS, for example), repeat it in the two locations above, or the page and its
  files go without it. Since `install.sh` rewrites `home.conf`, that is a change to make in the repository.
- `install.sh` overwrites your own edits to the site on the next update; put certificate settings only in `tls.conf`.

## Updating

The web UI: gear icon in the header → **Update now** → confirm. The button starts `home-update.service`, which runs
`/opt/home-ve/deploy/update.sh` as root as a service of its own, so the update runs to the end even though
`install.sh` restarts the backend along the way. The page shows the status and the script's output, and after a
successful update offers to reload itself (a new frontend may have arrived). From the console: the same script,
`/opt/home-ve/deploy/update.sh`, or `systemctl restart home-update` and `journalctl -u home-update -n 50`.

`update.sh` brings the host to the latest version of its **channel** and then runs that version's `install.sh`,
which downloads that version's build (backend and frontend, made by CI from the sources) from the repository's
GitHub releases and checks its SHA-256: the host needs no .NET or Node. CI makes the build a few minutes after a
commit is pushed or a release is published; an update before that changes nothing and says so: try again a little
later.

| Channel | Follows | For |
|---|---|---|
| **Stable** (default) | the branch `stable`: the latest release, a `vX.Y.Z` tag | every host that should just work |
| **Dev** | the branch `main`: every commit, as soon as it is pushed | trying what comes next; now and then something is broken |

Switch in the same card (Stable / Dev); it takes effect at the next update. Next to it is the version running now:
`v0.1.0` is a release, `v0.1.0-3-gabc1234` is 3 commits after it. The channel is kept in
`/var/lib/home-backend/update-channel`; `update.sh` accepts only `stable` or `dev` from it, anything else counts as
stable.

A host never goes back on its own: when it runs something newer than its channel (it was on dev, now it is on
stable), `update.sh` says so and leaves it where it is until the channel catches up.

If `vm-run` or `vm@.service` changed, VMs pick them up on their next restart.

## Rollback

Back to an earlier release, say `v0.1.0` (`git -C /opt/home-ve tag` lists them):

```bash
git -C /opt/home-ve checkout --detach v0.1.0
bash /opt/home-ve/deploy/install.sh
```

`install.sh` downloads the build of that release (up to v0.4.0, the build came in git with the release itself).

The next update brings the host forward again to the latest version of its channel.

## Security

- Only nginx is reachable from outside. Kestrel listens on `127.0.0.1:5000`.
- Requests not from `AllowedNetworks` (by default the private networks `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` and the
  host itself) are dropped without a response. To narrow this down to your own networks, set `AllowedNetworks` in `config.json`.
- Login is by password; a PBKDF2 hash is stored in `/etc/home-backend/config.json`. The session cookie lasts 7 days and is `Secure`
  (nginx passes `X-Forwarded-Proto`). At most 10 login attempts per 5 minutes from one IP.
- The backend does not run as root. For exactly what it is allowed to do (polkit, `/etc/vm`, the console socket), see
  [architecture.md](architecture.md#permissions-who-can-do-what).
- `vm-run` runs as root and does not trust `.conf` files: it parses them rather than executing them, and accepts only
  thin volumes of the `data` pool as disks.
- The console is the VM's keyboard and screen. Anyone logged in to the web UI can log in to a VM if they know its password,
  or reboot it into single-user mode. The web UI password should be at least as strong as the root passwords of your VMs.

## Settings

File `/etc/home-backend/config.json` (template: `deploy/config.example.json`). After editing, run
`systemctl restart home-backend`.

| Key | Default | |
|---|---|---|
| `Urls` | `http://127.0.0.1:5000` | where Kestrel listens; nginx proxies `/api/` here |
| `AllowedNetworks` | `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `127.0.0.0/8`, `::1/128` | who is allowed in |
| `DiskGroup` | — (found by `install.sh`) | LVM volume group with the `data` thin pool: a new VM's disk is `/dev/<group>/<name>` |
| `VmConfigDir` | `/etc/vm` | where the `.conf` files are (vm-run always reads `/etc/vm`) |
| `VmRuntimeDir` | `/run` | where the `vm-<name>` folders with sockets are |
| `DataDir` | `/var/lib/home-backend` | cookie keys |
The web UI password is not in this file: the first-run setup keeps its hash (PBKDF2) in
`/var/lib/home-backend/password`, readable by the backend and root only. To change it, or reset a forgotten one,
delete that file and restart the backend: the web UI shows the setup again, with a new setup code.

```bash
rm /var/lib/home-backend/password && systemctl restart home-backend
cat /var/lib/home-backend/setup-code
```

## New VM

1. **ISO images → Download an ISO**: a link to an installation image, for example Debian netinst. The host downloads it
   itself to `/var/lib/home-backend/iso`, so you can close the page on your phone.
2. **New VM**: name, cores, memory, disk size, ISO in the CD drive, network cards. With "Start now" checked, the VM
   starts right away and the console with the installer opens.
3. On first start, `vm-run` creates the disk `/dev/<DiskGroup>/<name>` (a thin volume in the `data` pool): space is used
   as data is written. An empty disk is not bootable, so the BIOS boots from the CD. After installation the disk boots, so you
   can leave the ISO in place or remove it in Settings → CD drive.

**The VM's addresses** show on its card and on its Summary tab once `qemu-guest-agent` runs in the guest (Debian,
Ubuntu: `apt install qemu-guest-agent`, it starts by itself; Windows: the guest agent from the virtio-win ISO). The
host asks it every 15 seconds, so a new address shows up within that. The card shows one: an address in a network the host itself has on
a bridge the VM is plugged into (the one you reach it at from the host and your LAN, not the guest's docker0 or VPN);
without such a network, the guest's first IPv4. The Summary tab lists all of them. The same agent lets backups freeze the guest's
file systems (see Backups), so it is worth installing in every VM.

VMs boot via BIOS (SeaBIOS). Installers support this, but you must install the system in BIOS/MBR mode.

**Deleting**: VM → Settings → Delete VM, only when the VM is stopped. Without the checkbox only the `.conf` is deleted; the disk stays,
and a new VM with the same name picks it up as is. With the checkbox the disk is deleted too; for that you must type the VM's name.

## Backups

Where: a separate volume mounted at `/var/backups/vm`. Until it is mounted, `install.sh` does not enable the timer, and `vm-backup` refuses to write,
so the root filesystem does not fill up.

Create the volume, for example a regular (not thin) LV in your volume group, or use a separate disk. Format it as ext4,
mount it at `/var/backups/vm` with an `/etc/fstab` line that uses `nofail`, then run `install.sh` again to enable the
nightly timer.

How big: a backup is the VM disk's used space, compressed (a fresh Debian is well under 1 GB), and up to 11 are kept
per VM (7 daily, 4 weekly; consecutive ones are mostly the same data, but each is stored in full). Check what is free
first: `vgs` (`VFree`). If the pool already took the space, use a separate disk, or a smaller volume.

A backup on the same disk protects against mistakes (a broken update, a deleted file), not against the disk dying:
for that there is the [offsite copy](#offsite-copy-cloudflare-r2).

Replace `<group>` with your volume group's name and `50G` with the size you chose:

```bash
lvcreate -L 50G -n backups <group>
mkfs.ext4 /dev/<group>/backups
mkdir -p /var/backups/vm
echo '/dev/<group>/backups /var/backups/vm ext4 defaults,nofail 0 2' >> /etc/fstab
mount /var/backups/vm
bash /opt/home-ve/deploy/install.sh
```

How (`deploy/vm/vm-backup`, unit `vm-backup@<name>`):

1. if the VM is running and has `qemu-guest-agent`, the guest's filesystems are frozen for a moment (fsfreeze);
   without the agent the copy is like after pulling the power cord, which journaling filesystems survive;
2. a thin snapshot of the disk is taken: instant and takes no space;
3. the guest is unfrozen and keeps running, and the snapshot is compressed with `zstd` to
   `/var/backups/vm/<name>/<name>-<YYYYMMDD-HHMMSS>.img.zst` (next to it: `.conf`, the VM's settings at that moment, and `.info`, the disk size);
4. the snapshot is deleted. Retention keeps the most recent backup for each of the last 7 days and for each of the 4 weeks before that.

When: every night around 3:30 in the host's time zone (`vm-backup-all.timer`, up to 30 minutes later; a night the host was off is caught up at the next boot) for all VMs without `BACKUP=no`, and with the
VM → Backups → Back up now button. The "Back up every night" checkbox in the VM's settings turns the nightly backup on and off.
Check the host's time zone with `timedatectl`; set yours with `timedatectl set-timezone Europe/Berlin` (the list:
`timedatectl list-timezones`).

Restore: VM → Backups → Restore, only for a stopped VM, and you must type its name. The disk is overwritten with
the backup, and what was on it before is kept as the snapshot `<group>/<name>-undo` until the next restore.
While a restore is running, the VM does not start.

```bash
systemctl list-timers vm-backup-all              # when the next nightly run is
systemctl restart vm-backup@router               # manual backup
journalctl -u vm-backup@router -n 30             # how it went
ls -lh /var/backups/vm/router/                   # what is stored
systemctl start vm-restore@router:20261008-033512   # restore (the VM must be stopped)
lvconvert --merge <group>/router-undo            # undo the restore (VM stopped)
```

## Offsite copy (Cloudflare R2)

A second copy of the backups outside your home: all of `/var/backups/vm` and an archive of the host's settings (`/etc`, `/root/.ssh`,
`/var/lib/home-backend` without ISOs) go to an R2 bucket, **encrypted on the host** (rclone crypt: both the data
and the file names are encrypted). This is done by `vm-offsite` (`deploy/offsite`), unit `vm-offsite.service`: every night after the VM backups,
and with the Settings → Offsite backup → **Upload now** button.

In the cloud:
- `vm/` is a mirror of `/var/backups/vm` (whatever local retention deleted is deleted here too, but first…);
- `trash/<time>/` …it moves here and stays for another 30 days.

Safeguards (`/etc/vm-offsite/offsite.conf`, template `offsite.conf.example` next to it): at most 5 GB per run,
nothing is uploaded if more than 60 GiB is stored locally or in the cloud, at most 20 deletions per run, bandwidth limited to 20 Mbit/s.
If your router can do it, a monthly quota for the host's traffic to Cloudflare is a good extra safeguard (the author uses an nftables quota on the router).
rclone uses IPv4 only (`--bind 0.0.0.0`) so that such a router quota can see it.

### Setup (once)

`install.sh` installs rclone from rclone.org if it is missing or older than 1.65: rclone 1.60 from Debian 13 does not work with R2
(error `501 Not Implemented` on every file).

1. **Cloudflare → R2 → Create bucket**: a name, for example `home-backups`, Location: **Europe (EU)**, class Standard.
2. **R2 → Manage API tokens → Create API token**: Object Read & Write, this bucket only. Write down the Access Key ID,
   the Secret Access Key and the endpoint `https://<account-id>.r2.cloudflarestorage.com` (for an EU bucket:
   `https://<account-id>.eu.r2.cloudflarestorage.com`).
3. On the host, enter the keys yourself (they will not stay in the shell history):
   ```bash
   rclone config --config /etc/vm-offsite/rclone.conf
   ```
   - `n` → name **`r2`** → type `s3` → provider `Cloudflare` → `access_key_id`, `secret_access_key` → endpoint from
     step 2 → everything else default. In the advanced config: **`no_check_bucket = true`** (a token for a single bucket cannot
     create buckets; without this, uploads fail with AccessDenied).
   - `n` → name **`offsite`** → type `crypt` → remote **`r2:home-backups`** → filename_encryption `standard` →
     directory_name_encryption `true` → password: `g` (generate) or your own → the second password (salt) the same way.
   - **Save both passwords outside the host right away** (in a password manager). Without them you cannot open the cloud backups if the host dies.
4. Check and run the first upload:
   ```bash
   chmod 600 /etc/vm-offsite/rclone.conf
   rclone --config /etc/vm-offsite/rclone.conf lsd r2:home-backups     # bucket is visible (empty is fine)
   systemctl restart vm-offsite && journalctl -u vm-offsite -f         # or Upload now in the web UI
   ```
   In Cloudflare the bucket will contain folders and files with unreadable names: this is expected.

### Restoring from the cloud

From any machine with rclone and the same `rclone.conf` (you can recreate it: step 3 with the same passwords):
```bash
rclone --config rclone.conf lsf -R offsite:vm/router                  # what is there
rclone --config rclone.conf copy offsite:vm/router/router-20261008-033512.img.zst /var/backups/vm/router/
```
The file arrives decrypted. Then use Restore in the web UI as usual (copy the `.conf` and `.info` next to the `.img.zst`).
Host settings: `offsite:vm/_host/host-<time>.tar.zst` → `tar --zstd -xf … -C /tmp/restore`, then put back what you need.

## Managing VMs by hand, without the web UI

```bash
systemctl status vm@router            # state
systemctl restart vm@router           # restart: shutdown via the power button, then start
qmp router system_powerdown           # "press the power button"
journalctl -u vm@router -n 50         # why it does not start: vm-run says which .conf line is wrong
nano /etc/vm/router.conf              # edit; applied on the next start
```

## Troubleshooting

```bash
systemctl status home-backend                       # is the service running
journalctl -u home-backend -n 50                    # its log
curl -i http://127.0.0.1:5000/api/auth/me           # backend directly: 401 = alive and waiting for login
curl -ik https://127.0.0.1/api/auth/me              # through nginx: also 401; 404 or 502 = nginx config
pkcheck --action-id org.freedesktop.systemd1.manage-units --process $(systemctl show -p MainPID --value home-backend) \
  --detail unit vm@router.service --detail verb restart   # does polkit allow the backend to restart it
```

- `pkcheck` or the buttons: "Could not connect: No such file or directory" → the D-Bus system bus is not running: `apt-get install dbus && systemctl start dbus polkit`.
- The buttons give "Interactive authentication required" → the polkit rule is missing or `polkitd` is not installed:
  `ls /etc/polkit-1/rules.d/`, `systemctl status polkit`.
- Console: "Failed to connect" and 502 → the VM was started by an old `vm-run` without the VNC socket: `systemctl restart vm@<name>`.
- Saving settings: "Permission denied" → `/etc/vm` has the wrong group: `bash /opt/home-ve/deploy/install.sh`.
