import { readdirSync, readFileSync } from 'node:fs'
import { parse } from 'vue/compiler-sfc'
import { NodeTypes } from '@vue/compiler-dom'
import type { AttributeNode, DirectiveNode, RootNode, TemplateChildNode } from '@vue/compiler-dom'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { createI18n } from 'vue-i18n'
import { EN } from '@/test/helpers/messages'

const OLD_TERM = /counterpart/i
const COMPANY_WORD = /\bcompan(?:y|ies)\b/i
const WORKFLOW_PLACEHOLDER = /\{'\{Counterparty\.[A-Za-z]+\}'\}/g
const LINKED_TERM = /@:\{'terms\.[A-Za-z]+'\}/g
const PARAMETER = /\{[A-Za-z]+\}/g
const SOURCE_ROOT = __dirname

type MessageTree = { [key: string]: string | string[] | MessageTree }

const leaves = (node: MessageTree | string | string[], path: string[] = []): [string, string][] => {
  if (typeof node === 'string') {
    return [[path.join('.'), node]]
  }
  return Object.entries(node).flatMap(([key, child]) => leaves(child as MessageTree, [...path, key]))
}

const messages = leaves(EN as unknown as MessageTree)
  .filter(([key]) => !key.startsWith('terms.'))
  .map(([key, message]): [string, string, string] => [
    key,
    message,
    message.replace(WORKFLOW_PLACEHOLDER, '').replace(LINKED_TERM, '').replace(PARAMETER, ''),
  ])

const sourceFiles = (extension: RegExp) =>
  (readdirSync(SOURCE_ROOT, { recursive: true }) as string[]).filter(
    file => extension.test(file) && !/\.test\.ts$/.test(file) && !file.startsWith('test/'),
  )

const HIDDEN_ATTRIBUTES = new Set([
  'class',
  'id',
  'name',
  'to',
  'href',
  'key',
  'ref',
  'type',
  'for',
  'aria-controls',
  'aria-describedby',
  'aria-labelledby',
])
const isHiddenAttribute = (name: string) => HIDDEN_ATTRIBUTES.has(name) || name.startsWith('data-')

const STRING_LITERAL = /(['"`])((?:\\.|(?!\1)[^\n\\])*)\1/g
const WHOLE_OLD_WORD = /\bcounterpart(?:y|ies)\b/i
const CAPITALISED_WORD = /^[A-Z][a-z]+[:.!?]?$/
const NEVER_SHOWN = /^\s*(?:\/\/|\*|\/\*)|console\.|throw new Error\(/
const WORKFLOW_PLACEHOLDER_NAMES_FILE = join('utils', 'placeholderValidation.ts')

const isShownLiteral = (literal: string) =>
  WHOLE_OLD_WORD.test(literal) && (/\s/.test(literal) || CAPITALISED_WORD.test(literal))

const shownLiteralsOf = (code: string) =>
  code
    .split('\n')
    .filter(line => !NEVER_SHOWN.test(line))
    .flatMap(line => [...line.matchAll(STRING_LITERAL)].map(([, , literal]) => literal!))
    .filter(isShownLiteral)

const shownTemplateTextOf = (node: RootNode | TemplateChildNode): string[] => {
  switch (node.type) {
    case NodeTypes.TEXT:
      return OLD_TERM.test(node.content) ? [node.content.trim()] : []
    case NodeTypes.INTERPOLATION:
      return shownLiteralsOf(node.content.loc.source)
    case NodeTypes.ROOT:
      return node.children.flatMap(shownTemplateTextOf)
    case NodeTypes.ELEMENT:
      return [...node.props.flatMap(shownAttributeTextOf), ...node.children.flatMap(shownTemplateTextOf)]
    default:
      return []
  }
}

const shownAttributeTextOf = (prop: AttributeNode | DirectiveNode): string[] => {
  if (prop.type === NodeTypes.DIRECTIVE) {
    return prop.exp ? shownLiteralsOf(prop.exp.loc.source) : []
  }
  return prop.value && !isHiddenAttribute(prop.name) && OLD_TERM.test(prop.value.content) ? [prop.value.content] : []
}

type Source = { file: string; shownTemplateText: string[]; script: string }

const readSource = (file: string): Source => {
  const source = readFileSync(join(SOURCE_ROOT, file), 'utf8')
  if (!file.endsWith('.vue')) {
    return { file, shownTemplateText: [], script: source }
  }
  const { descriptor } = parse(source, { filename: file })
  return {
    file,
    shownTemplateText: descriptor.template?.ast ? shownTemplateTextOf(descriptor.template.ast) : [],
    script: [descriptor.script?.content ?? '', descriptor.scriptSetup?.content ?? ''].join('\n'),
  }
}

const sources = sourceFiles(/\.(vue|ts|tsx|js|jsx)$/).map(readSource)

describe('screen text says Company, never Counterparty', () => {
  it('no English message says counterparty', () => {
    const offending = messages
      .filter(([, , words]) => OLD_TERM.test(words))
      .map(([key, message]) => `${key}: ${message}`)

    expect(offending).toEqual([])
  })

  it('every message that says company takes the word from the terms block', () => {
    const literal = messages
      .filter(([, , words]) => COMPANY_WORD.test(words))
      .map(([key, message]) => `${key}: ${message}`)

    expect(literal).toEqual([])
  })

  it('no component template shows counterparty as text, in a shown attribute or in an expression string', () => {
    const offending = sources.flatMap(({ file, shownTemplateText }) =>
      shownTemplateText.map(text => `${file}: ${text}`),
    )

    expect(offending).toEqual([])
  })

  it('no script hard-codes a label or sentence that says counterparty', () => {
    const offending = sources
      .filter(({ file }) => file !== WORKFLOW_PLACEHOLDER_NAMES_FILE)
      .flatMap(({ file, script }) => shownLiteralsOf(script).map(literal => `${file}: ${literal}`))

    expect(offending).toEqual([])
  })

  it('renders the term in titles, sentences and counts', () => {
    const { t } = createI18n({
      legacy: false,
      locale: 'en',
      messages: { en: EN },
    }).global

    expect(t('page.counterpartyManagement.title')).toBe('Company Management')
    expect(t('shared.menu.counterpartyManagement')).toBe('Companies')
    expect(t('page.counterpartyManagement.toast.saveError')).toBe(
      'The company could not be saved. Try again in a moment.',
    )
    expect(t('page.counterpartyManagement.count', { count: 1 }, 1)).toBe('1 company')
    expect(t('page.counterpartyManagement.count', { count: 2 }, 2)).toBe('2 companies')
  })
})
