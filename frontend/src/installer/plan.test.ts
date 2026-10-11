import { describe, expect, it } from 'vitest'
import type { Machine } from './api'
import { newForm, poolGb, sizesFor, staticFrom, toRequest, validate, type InstallForm } from './plan'

const GiB = 2 ** 30
const machine: Machine = {
  uefi: true,
  cpu: 'x',
  cpus: 4,
  memoryBytes: 16 * GiB,
  disks: [
    {
      name: 'sdb',
      sizeBytes: 16 * GiB,
      model: 'stick',
      serial: null,
      transport: 'usb',
      removable: true,
      rotational: false,
      inUse: true,
    },
    {
      name: 'nvme0n1',
      sizeBytes: 1000 * GiB,
      model: 'ssd',
      serial: null,
      transport: 'nvme',
      removable: false,
      rotational: false,
      inUse: false,
    },
    {
      name: 'sda',
      sizeBytes: 40 * GiB,
      model: 'small',
      serial: null,
      transport: 'sata',
      removable: false,
      rotational: false,
      inUse: false,
    },
  ],
  nics: [
    { name: 'wlp3s0', mac: 'm1', link: true, wireless: true, driver: null, addresses: [] },
    { name: 'enp2s0', mac: 'm2', link: false, wireless: false, driver: null, addresses: [] },
    { name: 'enp1s0', mac: 'm3', link: true, wireless: false, driver: null, addresses: ['192.168.178.57/24'] },
  ],
}

const filled = (patch: Partial<InstallForm> = {}): InstallForm => ({
  ...newForm(machine, 'Europe/Berlin'),
  user: 'alex',
  password: 'long enough',
  ...patch,
})

describe('the installer form', () => {
  it('starts on the first free disk and the connected wired card, with its address for a fixed one', () => {
    const f = newForm(machine, 'Europe/Berlin')
    expect([f.disk, f.nic, f.network]).toEqual(['nvme0n1', 'enp1s0', 'dhcp'])
    expect([f.address, f.gateway, f.dns]).toEqual(['192.168.178.57/24', '192.168.178.1', '192.168.178.1'])
    expect(validate(filled(), machine)).toEqual([])
  })

  it('sizes: a quarter for backups, swap as the memory up to 8 GB, and what fits on a small disk', () => {
    expect(sizesFor(machine.disks[1], machine.memoryBytes)).toEqual({ rootGb: 32, swapGb: 8, backupGb: 250 })
    const small = sizesFor(machine.disks[2], 2 * GiB)
    expect(small).toEqual({ rootGb: 16, swapGb: 2, backupGb: 10 })
    expect(poolGb(small, machine.disks[2])).toBe(10)
    expect(validate(filled({ disk: 'sda', ...small }), machine)).toEqual([])
  })

  it('says what is wrong', () => {
    expect(validate(filled({ disk: 'sdb' }), machine)).toEqual(['Disk: choose the one to install onto'])
    expect(validate(filled({ backupGb: 990 }), machine)).toEqual(['VMs: at least 8 GB must be left for them'])
    expect(validate(filled({ rootGb: NaN }), machine)).toContain('System: at least 8 GB')
    expect(validate(filled({ nic: 'wlp3s0' }), machine)).toEqual(['Network: choose a wired card'])
    expect(validate(filled({ hostname: 'Home' }), machine)).toHaveLength(1)
    expect(validate(filled({ user: 'root' }), machine)).toHaveLength(1)
    expect(validate(filled({ password: 'short' }), machine)).toEqual(['Password: at least 8 characters'])
    expect(validate(filled({ separatePasswords: true, userPassword: 'long enough' }), machine)).toEqual([
      "Root's password: at least 8 characters",
      "The web UI's password: at least 8 characters",
    ])
  })

  it('checks a fixed address', () => {
    const fixed = filled({ network: 'static' })
    expect(validate(fixed, machine)).toEqual([])
    expect(validate({ ...fixed, address: '192.168.178.57' }, machine)).toHaveLength(1)
    expect(validate({ ...fixed, address: '192.168.178.300/24' }, machine)).toHaveLength(1)
    expect(validate({ ...fixed, gateway: '' }, machine)).toHaveLength(1)
    expect(validate({ ...fixed, dns: '1.1.1.1, 8.8.8.8' }, machine)).toEqual([])
    expect(validate({ ...fixed, dns: 'dns.example' }, machine)).toHaveLength(1)
    expect(staticFrom(undefined)).toEqual({ address: '', gateway: '', dns: '' })
  })

  it('sends the one password as all three', () => {
    const r = toRequest(filled())
    expect([r.userPassword, r.rootPassword, r.webPassword]).toEqual(['long enough', 'long enough', 'long enough'])
    expect(r).not.toHaveProperty('password')
    expect(r).not.toHaveProperty('separatePasswords')
    const apart = toRequest(filled({ separatePasswords: true, userPassword: 'a', rootPassword: 'b', webPassword: 'c' }))
    expect([apart.userPassword, apart.rootPassword, apart.webPassword]).toEqual(['a', 'b', 'c'])
  })
})
