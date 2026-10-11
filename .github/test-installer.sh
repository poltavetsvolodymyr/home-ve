#!/usr/bin/env bash
# test-installer.sh <iso> <bios|uefi>: the installer image end to end, in QEMU with KVM.
#   1. boots the image from a CD, as a machine with an empty 40 GB disk; the code comes on the serial port;
#   2. installs through the page's API, as its form would: the first free disk and wired card, DHCP, the dev
#      channel, the web UI's password set;
#   3. waits for CI's build of the commit the install checked out (the first boot downloads it);
#   4. boots the disk alone; once the first boot is through, signs in to the web UI with that password.
# Everything goes to $OUT (default test-installer/): summary.log (what the workflow publishes), the install's
# log, the serial port of each boot, a screenshot of each; when the installed system fails, its journal (the disk
# mounted with qemu-nbd: sudo, lvm2). Needs qemu-system-x86, qemu-utils, ovmf (uefi), curl, jq, python3, gh
# (GH_TOKEN, GH_REPO).
set -euo pipefail

iso=$(realpath "${1:?usage: test-installer.sh <iso> <bios|uefi>}")
firmware=${2:?usage: test-installer.sh <iso> <bios|uefi>}
out=$(mkdir -p "${OUT:-test-installer}" && realpath "${OUT:-test-installer}")
here="$(cd "$(dirname "$0")" && pwd)"
port=8443
url=https://127.0.0.1:$port
jar=$out/cookies

log() { echo "[$(date -u +%H:%M:%S)] $*" | tee -a "$out/summary.log"; }
fail() {
  log "FAILED: $*"
  screenshot failed
  if [[ $vm_name == disk ]]; then
    log "the web UI answers: $(api -s -w ' (HTTP %{http_code})' "$url/api/auth/setup" 2>&1 || true)"
    stop
    inspect_disk
  fi
  exit 1
}

# after a failed boot from the disk: its files and journal, read on the runner (qemu-nbd, as root)
inspect_disk() {
  local mnt=$out/mnt
  sudo modprobe nbd max_part=8 || return 0
  sudo qemu-nbd --connect=/dev/nbd0 "$out/disk.qcow2" || return 0
  sleep 2
  sudo vgchange -q -ay home >/dev/null || true
  mkdir -p "$mnt"
  if sudo mount -o ro /dev/home/root "$mnt"; then
    {
      echo "----- the installed system -----"
      sudo ls -la "$mnt/var/lib/home-backend" "$mnt/root" "$mnt/etc/issue.d"
      sudo ls -la "$mnt/etc/systemd/system/multi-user.target.wants"
      for unit in home-ve-firstboot home-backend nginx systemd-networkd; do
        echo "----- journal: $unit -----"
        sudo journalctl -D "$mnt/var/log/journal" --no-pager -o short-monotonic -u "$unit" 2>&1 | tail -n 60
      done
    } >>"$out/summary.log" 2>&1
    sudo umount "$mnt"
  fi
  sudo vgchange -q -an home >/dev/null || true
  sudo qemu-nbd --disconnect /dev/nbd0 >/dev/null || true
}
api() { curl -sk --max-time 30 -b "$jar" -c "$jar" "$@"; }

qemu-img create -q -f qcow2 "$out/disk.qcow2" 40G
fw=()
if [[ $firmware == uefi ]]; then
  cp /usr/share/OVMF/OVMF_VARS_4M.fd "$out/vars.fd"
  fw=(-drive "if=pflash,format=raw,readonly=on,file=/usr/share/OVMF/OVMF_CODE_4M.fd"
    -drive "if=pflash,format=raw,file=$out/vars.fd")
fi

pid='' vm_name=''
vm() { # <name> <more qemu arguments>: starts it in the background
  local name=$1
  shift
  rm -f "$out/monitor"
  qemu-system-x86_64 -enable-kvm -machine q35 -cpu host -m 4096 -smp 2 "${fw[@]}" \
    -drive "file=$out/disk.qcow2,if=virtio,format=qcow2" \
    -nic "user,model=virtio-net-pci,hostfwd=tcp:127.0.0.1:$port-:443" \
    -serial "file:$out/$name-serial.log" -display none -monitor "unix:$out/monitor,server,nowait" "$@" &
  pid=$!
  vm_name=$name
}
stop() {
  [[ -n $pid ]] || return 0
  kill "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  pid=
}
trap stop EXIT

# the screen, as a PNG: QEMU writes PPM, python3 turns it into PNG
screenshot() {
  [[ -n $pid && -S $out/monitor ]] || return 0
  local ppm=$out/$vm_name-$1.ppm
  python3 -c 'import socket,sys; s=socket.socket(socket.AF_UNIX); s.connect(sys.argv[1]); s.sendall(("screendump "+sys.argv[2]+"\n").encode()); s.settimeout(3)
try:
    while s.recv(4096): pass
except Exception: pass' "$out/monitor" "$ppm" || return 0
  sleep 1
  python3 "$here/ppm2png.py" "$ppm" "${ppm%.ppm}.png" 2>/dev/null && rm -f "$ppm"
  true
}

# until <seconds> <what> <command...>: runs the command every 5 s until it succeeds
until_ok() {
  local limit=$1 what=$2 start=$SECONDS
  shift 2
  until "$@"; do
    kill -0 "$pid" 2>/dev/null || fail "QEMU stopped while waiting for $what"
    ((SECONDS - start < limit)) || fail "no $what after $limit s"
    sleep 5
  done
  log "$what (after $((SECONDS - start)) s)"
}

log "test: $(basename "$iso"), $firmware"

# 1. the live image
cdrom=(-drive "file=$iso,media=cdrom,readonly=on")
[[ $firmware == bios ]] && cdrom+=(-boot d)
vm live "${cdrom[@]}"
code_on_serial() { code=$(grep -ao 'home-ve installer code: [A-Z0-9-]*' "$out/live-serial.log" | tail -n 1 | cut -d' ' -f4) && [[ -n $code ]]; }
until_ok 600 "the code on the serial port" code_on_serial
page_up() { api -o /dev/null "$url/api/installer/session"; }
until_ok 300 "the installer page" page_up
screenshot page

[[ "$(api -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' -d "{\"code\":\"$code\"}" "$url/api/installer/session")" == 204 ]] ||
  fail "the code from the serial port didn't open the page"
api "$url/api/installer/machine" >"$out/machine.json"
disk=$(jq -r '[.disks[] | select(.inUse | not)][0].name // empty' "$out/machine.json")
nic=$(jq -r '[.nics[] | select(.wireless | not)][0].name // empty' "$out/machine.json")
[[ -n $disk && -n $nic ]] || fail "no disk or card in $(cat "$out/machine.json")"
log "machine: uefi $(jq -r .uefi "$out/machine.json"), disk $disk, card $nic"

# 2. the install
web_password=$(head -c 18 /dev/urandom | base64)
jq -n --arg disk "$disk" --arg nic "$nic" --arg web "$web_password" '{
  disk: $disk, rootGb: 12, swapGb: 1, backupGb: 4, hostname: "home-ve-test", timezone: "Europe/Berlin", nic: $nic,
  network: "dhcp", user: "tester", channel: "dev", rootPassword: "root-password", userPassword: "user-password",
  webPassword: $web }' >"$out/form.json"
status=$(api -o "$out/start.json" -w '%{http_code}' -H 'Content-Type: application/json' -d @"$out/form.json" "$url/api/installer/install")
[[ $status == 202 ]] || fail "the install didn't start ($status): $(cat "$out/start.json")"
log "install started"

start=$SECONDS last=
while :; do
  api "$url/api/installer/install" >"$out/status.json" || true
  state=$(jq -r '.state // empty' "$out/status.json" 2>/dev/null || true)
  current=$(jq -r '"[\(.step)/\(.steps)] \(.current)"' "$out/status.json" 2>/dev/null || true)
  [[ $current != "$last" ]] && log "  $current" && last=$current
  [[ $state == "done" || $state == failed ]] && break
  kill -0 "$pid" 2>/dev/null || fail "QEMU stopped while installing"
  ((SECONDS - start < 3600)) || fail "the install took more than an hour"
  sleep 10
done
# the whole log, from the live system's file (the API gives its tail)
jq -r '.log[]' "$out/status.json" >"$out/install-tail.log"
screenshot installed
[[ $state == "done" ]] || {
  tee -a "$out/summary.log" <"$out/install-tail.log"
  fail "the install failed"
}
log "install done in $((SECONDS - start)) s"
commit=$(grep -o 'commit [0-9a-f]\{40\}' "$out/install-tail.log" | cut -d' ' -f2 || true)
stop

# 3. the build the first boot downloads
if [[ -n $commit ]]; then
  start=$SECONDS
  until gh release view dev --json assets -q '.assets[].name' | grep -qx "home-ve-$commit-linux-x64.tar.gz"; do
    ((SECONDS - start < 1800)) || fail "no build of $commit in the dev pre-release after 30 min"
    sleep 20
  done
  log "the build of ${commit:0:7} is there"
else
  log "note: the install's log names no commit; not waiting for its build"
fi

# 4. the installed system, from its disk
boot=()
[[ $firmware == bios ]] && boot=(-boot c)
vm disk "${boot[@]}"
# the first boot downloads and installs home-ve, which can take a while; once its web UI answers, the password from
# the installer comes within a minute (firstboot.sh), so 5 minutes after the first answer is a failure
answered=
set_up() {
  local needs
  needs=$(api "$url/api/auth/setup" | jq -r '.needsSetup' 2>/dev/null) || true
  [[ $needs == false ]] && return 0
  if [[ $needs == true && -z $answered ]]; then
    answered=$SECONDS
    log "the web UI answers, still without a password"
  fi
  [[ -z $answered ]] || ((SECONDS - answered < 300)) || fail "the web UI still asks for its setup 5 minutes after it came up"
  return 1
}
until_ok 1500 "the web UI with its password set" set_up
screenshot web

[[ "$(api -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' -d '{"password":"not-the-password"}' "$url/api/auth/login")" == 401 ]] ||
  fail "a wrong password got in"
[[ "$(jq -n --arg p "$web_password" '{password: $p}' | api -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' -d @- "$url/api/auth/login")" == 204 ]] ||
  fail "the password set in the installer doesn't sign in"
[[ "$(api -o /dev/null -w '%{http_code}' "$url/api/auth/me")" == 200 ]] || fail "signed in, but no session"
log "signed in to the web UI with the installer's password"
log "PASSED"
