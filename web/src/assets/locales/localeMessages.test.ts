import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { createI18n } from 'vue-i18n'

import { DEFAULT_LOCALE, LANGUAGES } from './index'
import * as pageMessages from './en/page'
import * as sharedMessages from './en/shared'
import { EN } from '@/test/helpers/messages'

/**
 * Every locale message must compile with the vue-i18n message compiler.
 * Literal characters like { } | @ are syntax (interpolation, pluralization,
 * linked messages) and need the {'...'} literal escape, otherwise t() throws
 * at render time and unmounts whatever component referenced the key.
 */

type MessageTree = { [key: string]: string | MessageTree }

function collectLeafKeys(tree: MessageTree, prefix = ''): string[] {
  const keys: string[] = []
  for (const [key, value] of Object.entries(tree)) {
    const path = prefix ? `${prefix}.${key}` : key
    if (typeof value === 'string') {
      keys.push(path)
    } else {
      keys.push(...collectLeafKeys(value, path))
    }
  }
  return keys
}

describe('locale messages', () => {
  for (const [locale, messages] of Object.entries({ ...LANGUAGES, [DEFAULT_LOCALE]: EN })) {
    it(`every "${locale}" message compiles`, () => {
      const i18n = createI18n({
        legacy: false,
        locale,
        fallbackLocale: DEFAULT_LOCALE,
        messages: { [locale]: messages },
        warnHtmlMessage: false,
        missingWarn: false,
        fallbackWarn: false,
      })

      const failures: string[] = []
      for (const key of collectLeafKeys(messages as MessageTree)) {
        try {
          i18n.global.t(key)
        } catch (error) {
          failures.push(`${key}: ${(error as Error).message}`)
        }
      }

      expect(failures).toEqual([])
    })
  }

  /**
   * A namespace can be authored, exported, and still never reach the bundle: nothing but this
   * test connects `en/page/index.ts` to the `page` block of `en/index.ts`. A namespace that
   * falls out renders its raw keys on every screen that uses it: a page once shipped its title
   * as the raw key of its namespace.
   */
  it('bundles every authored namespace', () => {
    const bundled = LANGUAGES[DEFAULT_LOCALE] as unknown as {
      page: Record<string, unknown>
      shared: Record<string, unknown>
    }

    expect(Object.keys(pageMessages).filter(name => !(name in bundled.page))).toEqual([])
    expect(Object.keys(sharedMessages).filter(name => !(name in bundled.shared))).toEqual([])
  })

  it('has a message for every literal key the source asks for', () => {
    const SOURCE_ROOT = join(__dirname, '..', '..')
    const LITERAL_KEY = /(?<![\w.])\$?t\(\s*'((?:page|shared)\.[A-Za-z0-9_.]+)'/g
    const bundled = EN as unknown as MessageTree
    const resolves = (key: string) =>
      key.split('.').reduce<MessageTree | string | undefined>(
        (node, part) => (typeof node === 'object' ? node[part] : undefined),
        bundled,
      ) !== undefined

    const missing = new Set<string>()
    const sourceFiles = (readdirSync(SOURCE_ROOT, { recursive: true }) as string[]).filter(
      file => /\.(vue|ts)$/.test(file) && !/\.test\.ts$/.test(file),
    )
    for (const file of sourceFiles) {
      for (const [, key] of readFileSync(join(SOURCE_ROOT, file), 'utf8').matchAll(LITERAL_KEY)) {
        if (!resolves(key!)) {
          missing.add(`${file}: ${key}`)
        }
      }
    }

    expect([...missing]).toEqual([])
  })
})
