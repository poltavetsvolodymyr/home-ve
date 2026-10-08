import { describe, expect, it } from 'vitest'
import { answer, confirm, confirmStore } from './confirm'

describe('confirm', () => {
  it('resolves with the answer and closes', async () => {
    const asked = confirm({ title: 'Delete x?' })
    expect(confirmStore.current()?.title).toBe('Delete x?')
    answer(true)
    expect(await asked).toBe(true)
    expect(confirmStore.current()).toBeNull()
  })

  it('a new question cancels the one still open', async () => {
    const first = confirm({ title: 'First?' })
    const second = confirm({ title: 'Second?' })
    expect(await first).toBe(false)
    expect(confirmStore.current()?.title).toBe('Second?')
    answer(false)
    expect(await second).toBe(false)
  })
})
