import { expect, it } from 'vitest'
import { levelClass, levelLabel } from './logLevel'

it.each([
  [0, 'critical', 'ERR'],
  [3, 'critical', 'ERR'],
  [4, 'warning', 'WARN'],
  [5, '', 'INFO'],
  [6, '', 'INFO'],
  [7, '', 'DBG'],
])('priority %d is %s %s', (priority, className, label) => {
  expect(levelClass(priority)).toBe(className)
  expect(levelLabel(priority)).toBe(label)
})
