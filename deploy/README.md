# deploy

This is everything the host needs. The scripts arrive through git (`update.sh`); `app/` and `www/` are the build of
the checked-out commit, made by CI and downloaded by `fetch-build.sh` (they are not in git).
Details: `docs/deployment.md` in the repository on GitHub (the host's sparse checkout does not include it).

| | |
|---|---|
| `app/home-backend` | the backend, a single binary, .NET included. Listens on `127.0.0.1:5000`. `app/COMMIT`: the commit it was built from |
| `fetch-build.sh` | downloads the build of a commit (`app/`, `www/`) from the repository's GitHub releases and checks it; `install.sh` runs it |
| `www/` | the frontend; `install.sh` copies it to `/var/www/home`, nginx serves it from there |
| `home-backend.service` | systemd unit of the backend, installed into `/etc/systemd/system/` |
| `home-update.service` | `update.sh` as root; started by the Update button in the web UI |
| `config.example.json` | example of `/etc/home-backend/config.json` |
| `vm/vm-run` | starts a VM from `/etc/vm/<name>.conf`; installed into `/usr/local/sbin/` |
| `vm/vm-disk-remove`, `vm/vm-disk-remove@.service` | deletes the disk of a stopped VM (the Delete VM button with the "and disk" checkbox) |
| `vm/vm-stop` | VM shutdown for `vm@.service`: power button, 120 s wait, then pull the power cord |
| `vm/vm-backup`, `vm/vm-backup@.service` | VM backup: snapshot → `zstd` into `/var/backups/vm/<name>/`, pruning old ones |
| `vm/vm-backup-all`, `.service`, `.timer` | every night backs up all VMs without `BACKUP=no` |
| `vm/vm-restore`, `vm/vm-restore@.service` | writes a backup back to the disk of a stopped VM (the old disk is kept as the snapshot `<name>-undo`) |
| `vm/vm-backup-delete`, `vm/vm-backup-delete@.service` | deletes one backup |
| `vm/vm-guest-net`, `vm/vm-guest-net.service` | every 15 s reads the running VMs' addresses from their guest agents into `/run/vm-guest-net/` for the web UI |
| `offsite/vm-offsite`, `offsite/vm-offsite.service` | encrypted upload of backups and host settings to R2 (setup: docs/deployment.md) |
| `offsite/offsite.conf.example` | upload limits; installed into `/etc/vm-offsite/` |
| `vm/qmp` | sends one command to a VM's control socket: `qmp router system_powerdown` |
| `vm/vm@.service` | service template: one VM = `vm@<name>` |
| `vm/vm-autostart`, `vm/vm-autostart.service` | at boot starts VMs with `AUTOSTART=yes` |
| `vm/50-home-backend.rules` | polkit: what the backend may do with units (VMs, disk removal, backups, Update) |
| `nginx/home.conf` | nginx site (HTTPS, WebSocket for the console) |
| `install.sh` | first installation and applying any update; can be run any number of times |
| `update.sh` | the latest version of the update channel (stable or dev) + `install.sh` |

```bash
/opt/home-ve/deploy/update.sh                                  # update
journalctl -u home-backend -n 50                               # backend log
journalctl -u vm@router -n 50                                  # VM log (same as the Logs tab)
systemctl restart vm@router                                    # restart a VM (new settings, new vm-run)
/opt/home-ve/deploy/app/home-backend set-password /etc/home-backend/config.json && systemctl restart home-backend
cd /opt/home-ve && git reset --hard <hash> && bash deploy/install.sh   # roll back to version <hash>
```
