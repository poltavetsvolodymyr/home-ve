#!/bin/sh
# shellcheck disable=SC2016  # the $words below are partman's, for the recipe, not the shell's
# Run by the Debian installer (preseed/run in preseed.cfg) before it partitions the disk: the layout for this
# machine's firmware. A partition table (GPT) with, on UEFI, the EFI system partition, on BIOS, the small partition
# GRUB needs there; then /boot, then LVM with root (30 GB) and swap (2 GB). guided_size in preseed.cfg keeps the
# rest of the group free.
#   <min MB> <priority> <max MB> <type> <options> .
if [ -d /sys/firmware/efi ]; then
  first='538 538 1075 free $iflabel{ gpt } $reusemethod{ } method{ efi } format{ } .'
else
  first='1 1 1 free $iflabel{ gpt } method{ biosgrub } .'
fi
debconf-set partman-auto/expert_recipe "home-ve :: $first"' 1024 1024 1024 ext4 method{ format } format{ } use_filesystem{ } filesystem{ ext4 } mountpoint{ /boot } . 10240 30720 30720 ext4 $lvmok{ } lv_name{ root } method{ format } format{ } use_filesystem{ } filesystem{ ext4 } mountpoint{ / } . 1024 2048 2048 linux-swap $lvmok{ } lv_name{ swap } method{ swap } format{ } .'
