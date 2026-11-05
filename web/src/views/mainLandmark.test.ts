import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

const SOURCE_ROOT = join(__dirname, '..')
const LAYOUTS_DIR = join('components', 'layouts')
const MAIN_LANDMARK = /<main[\s>]|role=["']main["']|h\(\s*["']main["']/

const filesDeclaringMain = () =>
  (readdirSync(SOURCE_ROOT, { recursive: true }) as string[])
    .filter(file => /\.(vue|ts)$/.test(file) && !/\.test\.ts$/.test(file))
    .filter(file => MAIN_LANDMARK.test(readFileSync(join(SOURCE_ROOT, file), 'utf8')))

describe('main landmark', () => {
  it('comes only from the layout that wraps a page, never from a view inside it', () => {
    const declaring = filesDeclaringMain()

    expect(declaring).toContain(join(LAYOUTS_DIR, 'DefaultLayout', 'DefaultLayout.vue'))
    expect(declaring.filter(file => !file.startsWith(LAYOUTS_DIR))).toEqual([])
  })
})
