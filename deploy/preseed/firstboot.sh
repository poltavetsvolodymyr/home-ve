#!/bin/bash
# The first boot after an automated install (preseed.cfg), run once by home-ve-firstboot.service:
#   1. the backup volume: a quarter of the volume group "home" (at most half of what is free), ext4, mounted at
#      /var/backups/vm (docs/deployment.md "Backups");
#   2. the thin pool "data": 90% of what is left, the rest a reserve (docs/deployment.md "The data thin pool");
#   3. install.sh; once it went through, the service disables itself. If it didn't (no network?), the next boot
#      tries again, or run this by hand: /opt/home-ve/deploy/preseed/firstboot.sh
#   4. on the host's screen, with the login prompt: the setup code, next to the web UI's address. The login prompt
#      clears the screen before it draws, so the code goes where it draws from: /etc/issue.d, in a file only root
#      reads (agetty, which draws it, runs as root). Not to the output: that goes to the journal as well.
# Each step is skipped when its result is already there, so running it again is safe.
set -euo pipefail

VG=home

if vgs "$VG" &>/dev/null; then
  if ! lvs "$VG/backups" &>/dev/null; then
    read -r size free < <(vgs --noheadings --units m --nosuffix -o vg_size,vg_free "$VG")
    size=${size%.*} free=${free%.*}
    mb=$((size / 4))
    ((mb <= free / 2)) || mb=$((free / 2))
    echo "==> the backup volume: ${mb} MB"
    lvcreate -q -y -L "${mb}m" -n backups "$VG"
    mkfs.ext4 -q "/dev/$VG/backups"
    mkdir -p /var/backups/vm
    grep -q '/var/backups/vm' /etc/fstab ||
      echo "/dev/$VG/backups /var/backups/vm ext4 defaults,nofail 0 2" >>/etc/fstab
    mount /var/backups/vm
  fi
  if ! lvs "$VG/data" &>/dev/null; then
    echo "==> the thin pool data (90% of what is free)"
    lvcreate -q -y --type thin-pool -l 90%FREE -n data "$VG"
  fi
else
  echo "note: no volume group \"$VG\": no backup volume or pool made (docs/deployment.md)"
fi

/opt/home-ve/deploy/install.sh
systemctl disable home-ve-firstboot.service

code=/var/lib/home-backend/setup-code
if [[ -s $code ]]; then
  (
    umask 077
    printf 'Setup code for the first visit: %s\n\n' "$(cat "$code")" >/etc/issue.d/home-ve.setup.issue
  )
fi
