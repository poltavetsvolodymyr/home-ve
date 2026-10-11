import type { Disk, InstallRequest, Machine, Nic } from './api'

// The same limits as backend/HomeBackend/Installer/Install.cs (InstallPlan); the server checks again.
export const minRootGb = 8
export const minPoolGb = 8
export const minPasswordLength = 8
/** The BIOS boot, EFI and /boot partitions, and room for LVM's own data. */
export const partitionsGb = 2
const hostnamePattern = /^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$/
const userPattern = /^[a-z_][a-z0-9_-]{0,31}$/
const takenUsers = [
  'root',
  'daemon',
  'bin',
  'sys',
  'sync',
  'games',
  'man',
  'lp',
  'mail',
  'news',
  'uucp',
  'proxy',
  'www-data',
  'backup',
  'list',
  'irc',
  'nobody',
  'messagebus',
  'sshd',
  'home-backend',
  'libvirt-qemu',
  'libvirt-dnsmasq',
]

/** The form: the request plus one password for all three, unless the user wants them apart. */
export interface InstallForm extends InstallRequest {
  separatePasswords: boolean
  /** with separatePasswords off: the user's, root's and the web UI's */
  password: string
}

export const diskGb = (d: Disk) => Math.floor(d.sizeBytes / 2 ** 30)

/** The disks the system can go onto: not the installer's own stick. */
export const freeDisks = (m: Machine) => m.disks.filter(d => !d.inUse)

/** The cards the bridge can go onto: wired ones, those with a cable first. */
export const wiredNics = (m: Machine) =>
  m.nics.filter(n => !n.wireless).sort((a, b) => Number(b.link) - Number(a.link) || a.name.localeCompare(b.name))

/**
 * The sizes for a disk: the system 32 GB (16 on a small disk), swap as much as the memory up to 8 GB, the backup
 * volume a quarter of the disk, as firstboot.sh makes it; the rest is the VMs' pool. Small disks get what fits.
 */
export function sizesFor(disk: Disk, memoryBytes: number) {
  const total = diskGb(disk)
  const rootGb = total >= 256 ? 32 : 16
  const swapGb = Math.max(1, Math.min(8, Math.round(memoryBytes / 2 ** 30)))
  const free = total - partitionsGb - rootGb - swapGb - minPoolGb
  const backupGb = Math.max(0, Math.min(Math.floor(total / 4), free))
  return { rootGb, swapGb, backupGb }
}

/** What's left for the VMs (the thin pool takes 90% of it, the rest stays as a reserve). */
export const poolGb = (f: Pick<InstallRequest, 'rootGb' | 'swapGb' | 'backupGb'>, disk: Disk) =>
  diskGb(disk) - partitionsGb - f.rootGb - f.swapGb - f.backupGb

/** The form's starting point for this machine. */
export function newForm(m: Machine, timezone: string): InstallForm {
  const disk = freeDisks(m)[0]
  const nic = wiredNics(m)[0]
  return {
    disk: disk?.name ?? '',
    ...(disk ? sizesFor(disk, m.memoryBytes) : { rootGb: 32, swapGb: 8, backupGb: 0 }),
    hostname: 'home-ve',
    timezone,
    nic: nic?.name ?? '',
    network: 'dhcp',
    ...staticFrom(nic),
    user: '',
    channel: 'stable',
    rootPassword: '',
    userPassword: '',
    webPassword: '',
    separatePasswords: false,
    password: '',
  }
}

/** A fixed address starts as the one DHCP gave the card now, the gateway as .1 of its network. */
export function staticFrom(nic: Nic | undefined) {
  const address = nic?.addresses[0] ?? ''
  const ip = address.split('/')[0]
  const gateway = /^\d+\.\d+\.\d+\.\d+$/.test(ip) ? ip.replace(/\.\d+$/, '.1') : ''
  return { address, gateway, dns: gateway }
}

/** What the server gets: the one password copied into the three when they aren't apart. */
export function toRequest(f: InstallForm): InstallRequest {
  const { separatePasswords, password, ...request } = f
  if (separatePasswords) return request
  return { ...request, rootPassword: password, userPassword: password, webPassword: password }
}

const isIPv4 = (s: string) =>
  /^\d{1,3}(\.\d{1,3}){3}$/.test(s) && s.split('.').every(p => Number(p) <= 255 && String(Number(p)) === p)

/** What's wrong with the form, field by field; empty when it can be installed. */
export function validate(f: InstallForm, m: Machine): string[] {
  const errors: string[] = []
  const disk = m.disks.find(d => d.name === f.disk)
  if (!disk || disk.inUse) errors.push('Disk: choose the one to install onto')
  if (!Number.isInteger(f.rootGb) || f.rootGb < minRootGb) errors.push(`System: at least ${minRootGb} GB`)
  if (!Number.isInteger(f.swapGb) || f.swapGb < 0 || f.swapGb > 128) errors.push('Swap: 0–128 GB')
  if (!Number.isInteger(f.backupGb) || f.backupGb < 0) errors.push('Backups: 0 GB or more')
  if (disk && !disk.inUse && poolGb(f, disk) < minPoolGb)
    errors.push(`VMs: at least ${minPoolGb} GB must be left for them`)

  if (!hostnamePattern.test(f.hostname))
    errors.push('Host name: small letters, digits and dashes, not at the start or end')
  if (!f.timezone) errors.push('Time zone: choose one')

  const nic = m.nics.find(n => n.name === f.nic)
  if (!nic || nic.wireless) errors.push('Network: choose a wired card')
  if (f.network === 'static') {
    const [ip, prefix, ...rest] = f.address.split('/')
    if (!isIPv4(ip) || !/^\d+$/.test(prefix ?? '') || +prefix < 1 || +prefix > 32 || rest.length)
      errors.push('Address: with its prefix, e.g. 192.168.1.10/24')
    if (!isIPv4(f.gateway)) errors.push('Gateway: an address, e.g. 192.168.1.1')
    if (
      !f.dns
        .split(/[\s,]+/)
        .filter(Boolean)
        .every(isIPv4)
    )
      errors.push('DNS: addresses, e.g. 192.168.1.1 1.1.1.1')
  }

  if (!userPattern.test(f.user) || takenUsers.includes(f.user) || f.user.startsWith('systemd-'))
    errors.push('User: small letters, digits, - and _, starting with a letter, not one the system has (root…)')

  const passwords = f.separatePasswords
    ? [
        ['Your password', f.userPassword],
        ["Root's password", f.rootPassword],
        ["The web UI's password", f.webPassword],
      ]
    : [['Password', f.password]]
  for (const [name, p] of passwords) {
    if (p.length < minPasswordLength) errors.push(`${name}: at least ${minPasswordLength} characters`)
    else if (/[\p{Cc}]/u.test(p)) errors.push(`${name}: no line breaks or tabs`)
  }
  return errors
}

/** The time zones to offer: the browser's list, or its own zone alone where it has no list. */
export function timezones(current: string): string[] {
  const list = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : []
  return list.includes(current) ? list : [current, ...list]
}

/** This browser's time zone: on a phone, where the machine most likely is. */
export function browserTimezone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
  } catch {
    return 'UTC'
  }
}
