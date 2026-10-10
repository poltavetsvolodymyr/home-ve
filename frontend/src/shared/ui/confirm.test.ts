import { describe, expect, it } from 'vitest'
import { answer, confirm, confirmEdit, confirmStore } from './confirm'

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

describe('confirmEdit', () => {
  it('gives the text as changed in the dialog', async () => {
    const result = confirmEdit({ title: 'Paste?', editText: 'a\nb' })
    answer(true, 'a\nc')
    expect(await result).toBe('a\nc')
  })
  it('gives the text as it was when nothing changed it', async () => {
    const result = confirmEdit({ title: 'Paste?', editText: 'a\nb' })
    answer(true)
    expect(await result).toBe('a\nb')
  })
  it('gives null when cancelled', async () => {
    const result = confirmEdit({ title: 'Paste?', editText: 'a\nb' })
    answer(false)
    expect(await result).toBeNull()
  })
})
