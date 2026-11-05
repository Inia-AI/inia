import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

const REPO = join(__dirname, '..')

describe('app shell', () => {
  it.each(['index.html', 'preview.html'])('mounts %s into an element that claims no ARIA role or state', file => {
    const mountPoint = readFileSync(join(REPO, file), 'utf8').match(/<div id="app"[^>]*>/)?.[0]

    expect(mountPoint).toBe('<div id="app">')
  })
})
