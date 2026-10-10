#!/usr/bin/env bash
# build-iso.sh <branch> <out.iso>: the home-ve installer. Debian's own small installer image (netboot mini.iso,
# about 70 MB: the installer only, it downloads the system), with deploy/preseed in its initrd:
#   /preseed.cfg   the installer reads it by itself, so plain "Install" installs a home-ve host
#   /home-ve/      recipe.sh and late.sh, and "source": where preseed.cfg would be on GitHub, for <branch>
#                  (stable or main), which late.sh installs
# The kernel, the boot loaders and the rest of the image stay Debian's (Secure Boot keeps working): xorriso writes
# the image anew with the old boot records replayed, only /initrd.gz is replaced. Run by the workflows; needs
# curl, xorriso, cpio, gzip.
set -euo pipefail

branch=${1:?usage: build-iso.sh <branch> <out.iso>}
out=${2:?usage: build-iso.sh <branch> <out.iso>}
root="$(cd "$(dirname "$0")/.." && pwd)"
repo=${GITHUB_REPOSITORY:-poltavetsvolodymyr/home-ve}
IMAGES=${DEBIAN_IMAGES:-https://deb.debian.org/debian/dists/trixie/main/installer-amd64/current/images}

tmp=$(mktemp -d)
trap 'rm -rf -- "${tmp:?}"' EXIT

echo "==> $IMAGES/netboot/mini.iso"
curl -fsSL --retry 3 -o "$tmp/mini.iso" "$IMAGES/netboot/mini.iso"
curl -fsSL --retry 3 -o "$tmp/SHA256SUMS" "$IMAGES/SHA256SUMS"
want=$(awk '$2 == "./netboot/mini.iso" { print $1 }' "$tmp/SHA256SUMS")
have=$(sha256sum "$tmp/mini.iso" | cut -d' ' -f1)
[[ -n $want && $want == "$have" ]] || { echo "build-iso: mini.iso doesn't match Debian's SHA256SUMS" >&2; exit 1; }

# the installer's initrd, the one both boot menus (BIOS and UEFI) load
xorriso -osirrox on -indev "$tmp/mini.iso" -extract /initrd.gz "$tmp/initrd.gz" 2>/dev/null ||
  { echo "build-iso: no /initrd.gz in mini.iso:" >&2; xorriso -indev "$tmp/mini.iso" -ls / >&2; exit 1; }

# our files as a second cpio archive after Debian's: the kernel unpacks both, ours on top
mkdir -p "$tmp/add/home-ve"
install -m 0644 "$root/deploy/preseed/preseed.cfg" "$tmp/add/preseed.cfg"
install -m 0755 "$root/deploy/preseed/recipe.sh" "$root/deploy/preseed/late.sh" "$tmp/add/home-ve/"
echo "https://raw.githubusercontent.com/$repo/$branch/deploy/preseed/preseed.cfg" >"$tmp/add/home-ve/source"
(cd "$tmp/add" && find . | LC_ALL=C sort | cpio -o -H newc -R 0:0 --quiet | gzip -9n) >"$tmp/add.cpio.gz"
cat "$tmp/initrd.gz" "$tmp/add.cpio.gz" >"$tmp/initrd.new.gz"

rm -f -- "$out"
xorriso -indev "$tmp/mini.iso" -outdev "$out" -map "$tmp/initrd.new.gz" /initrd.gz -boot_image any replay \
  >"$tmp/xorriso.log" 2>&1 || { cat "$tmp/xorriso.log" >&2; exit 1; }
sha256sum "$out" | sed 's#  .*/#  #' >"$out.sha256"
echo "==> $out ($(du -m "$out" | cut -f1) MB): installs $branch"
