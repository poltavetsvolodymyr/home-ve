#!/usr/bin/env bash
# build-live.sh <build.tar.gz> <out.iso>: the home-ve installer image. A small Debian 13 that boots from a stick
# or a CD into RAM and serves the installer's page (home-backend installer) on https://<its address>/.
#   <build.tar.gz>  a home-ve build (app/ and www/, as .github/package.sh packs it): its backend runs the page
# Run as root on Debian 13 with network access; CI runs it in a privileged debian:trixie container. live-build
# does the work: the system from installer/live (packages, files, hooks), the kernel and the boot loaders
# (BIOS and UEFI, Secure Boot with Debian's signed shim and GRUB), the hybrid ISO for sticks and CD drives.
set -euo pipefail

build=$(realpath "${1:?usage: build-live.sh <build.tar.gz> <out.iso>}")
out=$(realpath -m "${2:?usage: build-live.sh <build.tar.gz> <out.iso>}")
here="$(cd "$(dirname "$0")" && pwd)"

apt-get update -q
apt-get install -y -q --no-install-recommends live-build xorriso ca-certificates

work=$(mktemp -d)
trap 'rm -rf -- "${work:?}"' EXIT
cd "$work"

lb config \
  --mode debian \
  --distribution trixie \
  --architectures amd64 \
  --archive-areas "main non-free-firmware" \
  --apt-recommends false \
  --binary-images iso-hybrid \
  --debian-installer none \
  --memtest none \
  --firmware-chroot false \
  --firmware-binary false \
  --iso-volume "HOME-VE" \
  --iso-application "home-ve installer" \
  --bootappend-live "boot=live quiet"

# live-build's own package list brings live-config, which logs a "user" in on every console (one this image
# doesn't have): out, ours lists live-boot itself
rm -f config/package-lists/live.list.chroot
cp -r "$here/live/package-lists/." config/package-lists/
cp -r "$here/live/hooks/." config/hooks/
cp -r "$here/live/includes.chroot/." config/includes.chroot/
# the page's backend and frontend, from the build
mkdir -p config/includes.chroot/opt/home-ve
tar -xzf "$build" -C config/includes.chroot/opt/home-ve

lb build
iso=$(find . -maxdepth 1 -name '*.iso' | head -n 1)
[[ -n $iso ]] || { echo "build-live: live-build made no image" >&2; exit 1; }
# Our boot menus over live-build's (installer/live/boot): plain, three entries, 5 s. Written into the finished
# image, the boot records replayed as they are (as .github/build-iso.sh does).
live=$(xorriso -indev "$iso" -ls /live 2>/dev/null)
if [[ $live != *"'vmlinuz'"* || $live != *"'initrd.img'"* ]]; then
  echo "build-live: no /live/vmlinuz or /live/initrd.img for the menus" >&2
  exit 1
fi
rm -f -- "$out"
xorriso -indev "$iso" -outdev "$out" \
  -map "$here/live/boot/isolinux.cfg" /isolinux/isolinux.cfg \
  -map "$here/live/boot/grub.cfg" /boot/grub/grub.cfg \
  -boot_image any replay
(cd "$(dirname "$out")" && sha256sum "$(basename "$out")" >"$(basename "$out").sha256")
echo "==> $out ($(du -m "$out" | cut -f1) MB)"
