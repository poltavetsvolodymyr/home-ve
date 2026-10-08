#!/usr/bin/env bash
# Sets up the VM web UI on the host "home", or brings an existing setup up to date. Run as root from the checkout:
#   /opt/home-ve/deploy/install.sh
# Safe to re-run: every step checks what is already there. deploy/update.sh runs it after each pull.
# Needs no .NET on the host: deploy/app/home-backend is self-contained.
set -euo pipefail

DEPLOY_DIR="$(cd "$(dirname "$0")" && pwd)"
CONFIG_DIR=/etc/home-backend
CONFIG="$CONFIG_DIR/config.json"
UNIT=/etc/systemd/system/home-backend.service
VM_DIR=/etc/vm
WWW=/var/www/home

main() {
  require_root
  note_unusual_location
  install_packages
  create_service_user
  create_config
  migrate_vm_configs
  install_vm_tools
  install_service
  install_frontend
  show_status
}

require_root() {
  if [[ $EUID -ne 0 ]]; then
    echo "run as root" >&2
    exit 1
  fi
}

note_unusual_location() {
  if [[ "$DEPLOY_DIR" != /opt/home-ve/deploy ]]; then
    echo "note: the systemd unit expects the checkout at /opt/home-ve (this one is at ${DEPLOY_DIR%/deploy})." >&2
    echo "      Edit WorkingDirectory/ExecStart in $UNIT if you keep it elsewhere." >&2
  fi
}

# QEMU and socat run the VMs; polkitd lets the backend start and stop them without being root.
# systemctl run by a non-root user talks to systemd over the system D-Bus, which a minimal
# (debootstrap) Debian may not have yet: dbus provides it.
install_packages() {
  local missing=()
  for p in qemu-system-x86 socat dbus polkitd zstd; do
    dpkg -s "$p" &>/dev/null || missing+=("$p")
  done
  if (( ${#missing[@]} )); then
    echo "==> installing ${missing[*]}"
    apt-get install -y --no-install-recommends "${missing[@]}"
  fi
  # installed as a dependency, dbus does not always get started: make sure both run now
  systemctl start dbus.service polkit.service
}

# service user: no shell, no home; reads the journal through the unit's SupplementaryGroups
create_service_user() {
  if ! id home-backend &>/dev/null; then
    echo "==> creating user home-backend"
    useradd --system --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin home-backend
  fi
}

# config with the password hash, readable by the service only
create_config() {
  install -d -m 0750 -o root -g home-backend "$CONFIG_DIR"
  if [[ ! -f "$CONFIG" ]]; then
    echo "==> creating $CONFIG"
    install -m 0640 -o root -g home-backend "$DEPLOY_DIR/config.example.json" "$CONFIG"
  fi
  if ! grep -q '"PasswordHash": *"pbkdf2' "$CONFIG"; then
    echo "==> set the web UI password"
    "$DEPLOY_DIR/app/home-backend" set-password "$CONFIG"
    chown root:home-backend "$CONFIG"
    chmod 0640 "$CONFIG"
  fi
}

# The first .conf files (October 2026) were shell: NETS="br-lan=MAC br-wan=MAC", sourced by vm-run,
# and a VM started at boot because its unit was enabled. Now: one NET=<bridge> <MAC> line per card,
# parsed rather than sourced, and AUTOSTART=yes, which vm-autostart.service reads.
migrate_vm_configs() {
  local conf name nets net autostart
  for conf in "$VM_DIR"/*.conf; do
    if [[ ! -f "$conf" ]] || ! grep -q '^NETS=' "$conf"; then continue; fi
    name=$(basename "$conf" .conf)
    echo "==> $conf: NETS= -> NET= lines"
    nets=$(sed -n 's/^NETS="\{0,1\}\([^"]*\)"\{0,1\}$/\1/p' "$conf")
    autostart=no
    if systemctl is-enabled --quiet "vm@$name.service" 2>/dev/null; then
      autostart=yes
      systemctl disable "vm@$name.service"
    fi
    cp -p "$conf" "$conf.bak"
    {
      grep -v -e '^NETS=' -e '^# network cards:' -e '^AUTOSTART=' "$conf"
      for net in $nets; do echo "NET=${net%%=*} ${net#*=}"; done
      echo "AUTOSTART=$autostart"
    } >"$conf.new"
    mv "$conf.new" "$conf"
    echo "    (the old file is $conf.bak)"
  done
}

# vm-run, qmp, the vm@ unit, autostart at boot, and what the backend may do to them
install_vm_tools() {
  echo "==> VM tools"
  install -m 0755 "$DEPLOY_DIR/vm/vm-run" "$DEPLOY_DIR/vm/qmp" "$DEPLOY_DIR/vm/vm-autostart" \
    "$DEPLOY_DIR/vm/vm-disk-remove" "$DEPLOY_DIR/vm/vm-stop" \
    "$DEPLOY_DIR/vm/vm-backup" "$DEPLOY_DIR/vm/vm-restore" "$DEPLOY_DIR/vm/vm-backup-delete" "$DEPLOY_DIR/vm/vm-backup-all" \
    /usr/local/sbin/
  install -m 0644 "$DEPLOY_DIR/vm/vm@.service" "$DEPLOY_DIR/vm/vm-autostart.service" \
    "$DEPLOY_DIR/vm/vm-disk-remove@.service" "$DEPLOY_DIR/vm/vm-backup@.service" "$DEPLOY_DIR/vm/vm-restore@.service" \
    "$DEPLOY_DIR/vm/vm-backup-delete@.service" "$DEPLOY_DIR/vm/vm-backup-all.service" "$DEPLOY_DIR/vm/vm-backup-all.timer" \
    /etc/systemd/system/
  install -m 0644 "$DEPLOY_DIR/vm/50-home-backend.rules" /etc/polkit-1/rules.d/
  # the backend writes the VM settings (temp file + rename, so it needs the directory)
  install -d -m 0775 -o root -g home-backend "$VM_DIR"
  systemctl daemon-reload
  systemctl enable vm-autostart.service
  # nightly backups, only once the backup volume is there (docs/deployment.md: "Бэкапы")
  if mountpoint -q /var/backups/vm; then
    install -d -m 0750 -g home-backend /var/backups/vm
    systemctl enable --now vm-backup-all.timer
  else
    echo "note: /var/backups/vm is not mounted, nightly backups stay off (docs/deployment.md)" >&2
  fi
}

# home-update.service: the Update button in the UI runs update.sh through it
install_service() {
  install -m 0644 "$DEPLOY_DIR/home-backend.service" "$UNIT"
  install -m 0644 "$DEPLOY_DIR/home-update.service" /etc/systemd/system/
  systemctl daemon-reload
  systemctl enable home-backend
  systemctl restart home-backend
}

# static files for nginx, swapped in whole so stale assets don't pile up
install_frontend() {
  echo "==> frontend -> $WWW"
  mkdir -p "$(dirname "$WWW")"
  rm -rf "$WWW.new" "$WWW.old"
  cp -r "$DEPLOY_DIR/www" "$WWW.new"
  chmod -R u=rwX,go=rX "$WWW.new"
  if [[ -d "$WWW" ]]; then
    mv "$WWW" "$WWW.old"
  fi
  mv "$WWW.new" "$WWW"
  rm -rf "$WWW.old"
}

show_status() {
  sleep 2
  systemctl --no-pager --lines=5 status home-backend || true
  echo
  echo "Done. The backend listens on 127.0.0.1:5000; nginx serves the UI (docs/deployment.md)."
  echo "A running VM gets the new vm-run (and its console) at its next restart: systemctl restart vm@<name>"
}

main "$@"
