import { describe, expect, it } from 'vitest'
import { setupProblem } from './setup'

describe('setupProblem', () => {
  it('lets a complete form through, the code with or without the dash', () => {
    expect(setupProblem('ABCD-EFGH', 'long enough', 'long enough')).toBeUndefined()
    expect(setupProblem(' abcdefgh ', 'long enough', 'long enough')).toBeUndefined()
  })

  it('says what is missing', () => {
    expect(setupProblem('ABCD', 'long enough', 'long enough')).toMatch(/8 characters/)
    expect(setupProblem('ABCD-EFGH', 'short', 'short')).toMatch(/at least 8/)
    expect(setupProblem('ABCD-EFGH', 'long enough', 'long enough!')).toMatch(/don't match/)
  })
})
