#!/usr/bin/env bash
# Sets up the VM web UI on a Debian host, or brings an existing setup up to date. Run as root from the checkout:
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
  set_disk_group
  migrate_vm_configs
  install_rclone
  install_vm_tools
  install_service
  install_frontend
  install_nginx
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

# QEMU and socat run the VMs on LVM thin volumes (thin_check from thin-provisioning-tools activates the pool
# at boot; without it the pool stays down); polkitd lets the backend start and stop them without
# being root. systemctl run by a non-root user talks to systemd over the system D-Bus, which a minimal
# (debootstrap) Debian may not have yet: dbus provides it. nginx serves the UI, openssl makes its first
# certificate; curl (with the CA certificates) fetches rclone.
install_packages() {
  local missing=()
  for p in qemu-system-x86 socat lvm2 thin-provisioning-tools dbus polkitd zstd curl ca-certificates nginx openssl; do
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

# A new VM's disk is a thin volume in the pool "data": the backend needs to know its volume group. Found once,
# while config.json has none; after that it is left as it is.
set_disk_group() {
  if grep -q '"DiskGroup": *"[^"]' "$CONFIG"; then return; fi
  local groups
  groups=$(lvs --noheadings -o vg_name -S 'lv_name=data && segtype=thin-pool' 2>/dev/null | tr -d ' ') || true
  if [[ -z $groups ]]; then
    echo "note: no LVM thin pool named \"data\": the web UI can't create VMs until there is one, e.g." >&2
    echo "      lvcreate --type thin-pool -l 90%FREE -n data <volume group>   (then run install.sh again)" >&2
    return
  fi
  if [[ $groups == *$'\n'* ]]; then
    echo "note: thin pools \"data\" in several volume groups (${groups//$'\n'/ }): set DiskGroup in $CONFIG" >&2
    return
  fi
  if [[ ! $groups =~ ^[A-Za-z0-9_.+-]+$ ]]; then
    echo "note: volume group '$groups' has an odd name: set DiskGroup in $CONFIG by hand" >&2
    return
  fi
  echo "==> VM disks go to the thin pool $groups/data"
  if grep -q '"DiskGroup"' "$CONFIG"; then
    sed -i "s/\"DiskGroup\": *\"\"/\"DiskGroup\": \"$groups\"/" "$CONFIG"
  else
    sed -i "s/^\( *\)\"HomeBackend\": *{\$/&\n\1  \"DiskGroup\": \"$groups\",/" "$CONFIG"
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
# rclone for the offsite upload. Debian's (1.60 in trixie) fails against Cloudflare R2 with "501 Not
# Implemented", so anything older than RCLONE_MIN is replaced by the current release from rclone.org (one .deb,
# no dependencies). apt never downgrades it afterwards: its version is the older one.
RCLONE_MIN=1.65
install_rclone() {
  local have
  have=$(rclone version 2>/dev/null | sed -n '1s/^rclone v\([0-9]*\.[0-9]*\).*/\1/p') || true
  if [[ -n $have && $(printf '%s\n' "$RCLONE_MIN" "$have" | sort -V | head -1) == "$RCLONE_MIN" ]]; then
    return
  fi
  echo "==> rclone ${have:-missing}: installing the current release from rclone.org (R2 needs $RCLONE_MIN or newer)"
  local tmp
  tmp=$(mktemp -d)
  curl -fsSL -o "$tmp/rclone.deb" "https://downloads.rclone.org/rclone-current-linux-$(dpkg --print-architecture).deb"
  dpkg -i "$tmp/rclone.deb"
  rm -rf -- "${tmp:?}"
}

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
  # nightly backups, only once the backup volume is there (docs/deployment.md: "Backups")
  if mountpoint -q /var/backups/vm; then
    install -d -m 0750 -g home-backend /var/backups/vm
    systemctl enable --now vm-backup-all.timer
  else
    echo "note: /var/backups/vm is not mounted, nightly backups stay off (docs/deployment.md)" >&2
  fi

  # offsite upload (docs/deployment.md, "Offsite copy"): the script and unit always; it does nothing until
  # /etc/vm-offsite/rclone.conf is set up by hand. The directory is root's, but home-backend may see whether
  # rclone.conf is there (x for others, no r) to tell "not set up" in the web UI.
  install -m 0755 "$DEPLOY_DIR/offsite/vm-offsite" /usr/local/sbin/
  install -m 0644 "$DEPLOY_DIR/offsite/vm-offsite.service" /etc/systemd/system/
  install -d -m 0711 /etc/vm-offsite
  install -m 0644 "$DEPLOY_DIR/offsite/offsite.conf.example" /etc/vm-offsite/
  systemctl daemon-reload
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

# The site is ours and replaced on every run; the certificate is yours. /etc/nginx/home-ve/tls.conf names it:
# written once, then never touched again, except to renew the self-signed certificate it starts with.
# A site set up by hand before (docs/deployment.md, older versions) hands its certificate over to tls.conf.
NGINX_SITE=/etc/nginx/sites-available/home
NGINX_DIR=/etc/nginx/home-ve
install_nginx() {
  local tls=$NGINX_DIR/tls.conf
  install -d -m 0755 "$NGINX_DIR"
  if [[ ! -f $tls ]]; then
    if [[ -f $NGINX_SITE ]] && grep -Eq '^\s*ssl_certificate(_key)?\s' "$NGINX_SITE"; then
      echo "==> nginx: keeping the certificate of the existing site ($tls)"
      grep -E '^\s*ssl_certificate(_key)?\s' "$NGINX_SITE" | sed -E 's/^\s+//' >"$tls"
    else
      echo "==> nginx: a self-signed certificate until there is a real one ($tls)"
      printf 'ssl_certificate     %s;\nssl_certificate_key %s;\n' "$NGINX_DIR/selfsigned.crt" "$NGINX_DIR/selfsigned.key" >"$tls"
    fi
  fi
  # still on the self-signed one: make it (again) when it is missing or has less than 30 days left
  if grep -q selfsigned.crt "$tls" &&
    ! openssl x509 -checkend $((30 * 86400)) -noout -in "$NGINX_DIR/selfsigned.crt" &>/dev/null; then
    make_self_signed
  fi

  if [[ -f $NGINX_SITE ]]; then cp -p "$NGINX_SITE" "$NGINX_SITE.bak"; fi
  install -m 0644 "$DEPLOY_DIR/nginx/home.conf" "$NGINX_SITE"
  ln -sf "$NGINX_SITE" /etc/nginx/sites-enabled/home
  # Debian's welcome page wants to be the default server too
  rm -f /etc/nginx/sites-enabled/default
  if nginx -t -q; then
    systemctl enable --quiet nginx
    systemctl reload-or-restart nginx
  else
    echo "nginx rejects the new site: back to the old one ($NGINX_SITE.bak). The UI is not updated in nginx." >&2
    if [[ -f $NGINX_SITE.bak ]]; then mv "$NGINX_SITE.bak" "$NGINX_SITE"; fi
    return 1
  fi
}

# For the host's name and every address it has; the browser warns once, the connection is still encrypted.
# 825 days: the longest Apple devices accept for a certificate you choose to trust.
make_self_signed() {
  local san ip
  san="DNS:$(hostname)"
  for ip in $(hostname -I); do san+=",IP:$ip"; done
  (
    umask 077
    openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:prime256v1 -nodes -days 825 -subj "/CN=$(hostname)" \
      -addext "subjectAltName=$san" -keyout "$NGINX_DIR/selfsigned.key" -out "$NGINX_DIR/selfsigned.crt" 2>/dev/null
  )
  chmod 0644 "$NGINX_DIR/selfsigned.crt"
}

show_status() {
  sleep 2
  systemctl --no-pager --lines=5 status home-backend || true
  echo
  echo "Done. The web UI: https://$(hostname -I | cut -d' ' -f1)/ (docs/deployment.md)."
  echo "A running VM gets the new vm-run (and its console) at its next restart: systemctl restart vm@<name>"
}

main "$@"
