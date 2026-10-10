#!/bin/bash
# late.sh <preseed URL>: the end of an automated Debian install (preseed.cfg), run inside the new system before
# its first boot. The URL is where preseed.cfg came from, or (the ISO) where it would have: its branch is the one
# installed. Nothing here starts a service (the installer's chroot has no systemd running):
#   1. the checkout in /opt/home-ve, from the branch the preseed came from (stable, or main), as in
#      docs/deployment.md "First installation";
#   2. the bridge br0 on the card the installer used, by DHCP (docs/deployment.md "Bridge for VM networking");
#   3. home-ve-firstboot.service, which makes the backup volume and the pool and runs install.sh on the first boot.
# Its output: /var/log/home-ve-install.log.
set -euo pipefail

main() {
  url=${1:-}
  branch=$(sed -n 's#.*/home-ve/\([^/]*\)/deploy/preseed/[^/]*$#\1#p' <<<"$url")
  [[ $branch =~ ^[A-Za-z0-9._-]+$ ]] || branch=stable
  echo "==> home-ve from the branch $branch"

  if [[ ! -d /opt/home-ve/.git ]]; then
    git clone --quiet --filter=blob:none --sparse --branch "$branch" https://github.com/poltavetsvolodymyr/home-ve.git /opt/home-ve
    git -C /opt/home-ve sparse-checkout set deploy
  fi

  # The card: the one the installer configured by DHCP in /etc/network/interfaces. A static address stays as it
  # is, without a bridge: then set one up by hand (docs/deployment.md).
  nic=$(sed -n -E 's/^iface ([^ ]+) inet dhcp.*/\1/p' /etc/network/interfaces | grep -vx lo | head -n 1 || true)
  if [[ -n $nic ]]; then
    echo "==> bridge br0 on $nic"
    printf '[NetDev]\nName=br0\nKind=bridge\n' >/etc/systemd/network/10-br0.netdev
    printf '[Match]\nName=br0\n\n[Network]\nDHCP=ipv4\n\n[DHCPv4]\nClientIdentifier=mac\n' >/etc/systemd/network/10-br0.network
    printf '[Match]\nName=%s\n\n[Network]\nBridge=br0\n' "$nic" >/etc/systemd/network/20-br0-port.network
    cp /etc/network/interfaces /etc/network/interfaces.bak
    sed -i -E "s/^(allow-hotplug|auto) $nic\$/# &/; s/^iface $nic .*/# &/" /etc/network/interfaces
    systemctl enable systemd-networkd
  else
    echo "note: no card configured by DHCP: no bridge made. VMs get a network once there is one (docs/deployment.md)"
  fi

  install -m 0644 /opt/home-ve/deploy/preseed/home-ve-firstboot.service /etc/systemd/system/
  systemctl enable home-ve-firstboot.service
  echo "==> on the first boot: the backup volume, the pool \"data\" and install.sh (home-ve-firstboot.service)"
}

main "$@" 2>&1 | tee -a /var/log/home-ve-install.log
