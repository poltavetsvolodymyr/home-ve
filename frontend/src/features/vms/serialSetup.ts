/**
 * Commands that make a Debian or Ubuntu VM use its serial port (the Terminal tab), typed into it on the Console's
 * keyboard from a root shell: the kernel's messages there from the next boot on (once in GRUB's settings, even
 * typed twice), and a login prompt there right away.
 */
export const serialSetupCommands = [
  "grep -q console=ttyS0 /etc/default/grub || sed -i 's/^GRUB_CMDLINE_LINUX=\"/&console=tty0 console=ttyS0,115200 /' /etc/default/grub",
  'update-grub',
  'systemctl enable --now serial-getty@ttyS0.service',
].join('\n')
