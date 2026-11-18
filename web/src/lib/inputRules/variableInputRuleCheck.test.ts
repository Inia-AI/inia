import { createHash } from 'node:crypto'
import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

import type { WorkflowVariable } from '@/api/schema'
import { checkInputRules, withoutNumbering, type InputRuleWorkflow } from './variableInputRuleCheck'
import fixture from './variable-input-rule-cases.json'

const FIXTURE_SHA256 = 'a8d1ca2d17ec03d9a4ee52660dc67021cc3a8acf0e0368713a5878690d644e4a'
const FIXTURE_PATH = resolve(__dirname, 'variable-input-rule-cases.json')
const PLATFORM_COPY = resolve(process.cwd(), '../IniaAi.Platform/IniaAi.Domain.Tests/InputRules/Fixtures/variable-input-rule-cases.json')

const textOf = (path: string) => readFileSync(path, 'utf8').replace(/\r\n/g, '\n')

const workflows = fixture.workflows as unknown as Record<string, InputRuleWorkflow & { variables: WorkflowVariable[] }>

describe('variable input rules, shared cases', () => {
  it.each(fixture.cases.map(testCase => [testCase.name, testCase] as const))('%s', (_name, testCase) => {
    const outcome = checkInputRules(
      workflows[testCase.workflow],
      testCase.values as Record<string, unknown>,
      testCase.attached,
    )

    expect(outcome.violations.map(violation => ({ variable: violation.replacementTag, message: violation.message })))
      .toEqual(testCase.violations)
    expect(outcome.skipped.map(skipped => ({ variable: skipped.replacementTag, kind: skipped.kind, reason: skipped.reason })))
      .toEqual(testCase.skipped)
  })

  it('shows display names without their leading numbering only', () => {
    for (const name of fixture.names) {
      expect(withoutNumbering(name.displayName)).toBe(name.shown)
    }
  })

  it('keeps the cases equal to the pinned hash and to the platform copy', () => {
    const cases = textOf(FIXTURE_PATH)
    expect(createHash('sha256').update(cases, 'utf8').digest('hex')).toBe(FIXTURE_SHA256)
    if (existsSync(PLATFORM_COPY)) {
      expect(textOf(PLATFORM_COPY)).toBe(cases)
    }
  })
})
