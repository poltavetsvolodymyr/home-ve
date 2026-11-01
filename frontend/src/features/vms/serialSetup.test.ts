import { describe, expect, it } from 'vitest'
import { serialSetupCommands } from './serialSetup'

describe('serialSetupCommands', () => {
  // typed on a US keyboard by keysym: anything else may come out as something else in the VM
  it('is plain printable ASCII, one command a line', () => {
    for (const line of serialSetupCommands.split('\n')) expect(line).toMatch(/^[\x20-\x7e]+$/)
  })

  it('touches GRUB only once, even typed twice', () =>
    expect(serialSetupCommands).toMatch(/grep -q console=ttyS0 .* \|\| sed/))
})
