import { describe, expect, it } from 'vitest'
import { downloadPercent } from './progress'

describe('downloadPercent', () => {
  it('rounds down, so 100 means done', () => expect(downloadPercent({ receivedBytes: 999, totalBytes: 1000 })).toBe(99))
  it('is null without a size', () => expect(downloadPercent({ receivedBytes: 5, totalBytes: null })).toBeNull())
})
