import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

const SOURCE_ROOT = __dirname
const EMPTY_HEADER_CELL = /<(th|TableHead)\b[^>]*>\s*<\/\1>|<(th|TableHead)\b[^>]*\/>/g

const templates = () =>
  (readdirSync(SOURCE_ROOT, { recursive: true }) as string[]).filter(file => file.endsWith('.vue'))

describe('table header cells', () => {
  it('give every column a caption, visible or for screen readers only', () => {
    const empty = templates().flatMap(file =>
      [...readFileSync(join(SOURCE_ROOT, file), 'utf8').matchAll(EMPTY_HEADER_CELL)].map(match => `${file}: ${match[0]}`),
    )

    expect(empty).toEqual([])
  })
})
