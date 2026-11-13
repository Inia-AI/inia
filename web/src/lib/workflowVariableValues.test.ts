import { describe, expect, it } from 'vitest'
import { z } from 'zod'

import type { WorkflowVariable } from '@/api/schema'
import { WorkflowVariableTypes } from '@/enums'
import {
  appliedTemplateDefault,
  defaultVariableValues,
  isBlankVariableValue,
  numberVariableSchema,
  submittableVariables,
} from './workflowVariableValues'

const variable = (
  replacementTag: string,
  type: WorkflowVariableTypes,
  extra: Partial<WorkflowVariable> = {},
): WorkflowVariable => ({ id: replacementTag, replacementTag, displayName: replacementTag, type, ...extra })

describe('isBlankVariableValue', () => {
  it.each([undefined, null, '', '   ', Number.NaN])('treats %s as blank', value => {
    expect(isBlankVariableValue(value)).toBe(true)
  })

  it.each([0, 12.5, 'text', [], ['a']])('treats %s as a value', value => {
    expect(isBlankVariableValue(value)).toBe(false)
  })
})

describe('numberVariableSchema', () => {
  const formOf = (isRequired: boolean) => z.object({ amount: numberVariableSchema(isRequired) })

  it.each([undefined, '', null, Number.NaN])('accepts a blank optional number (%s) as no value', value => {
    const result = formOf(false).safeParse({ amount: value })

    expect(result.success).toBe(true)
    expect(result.data?.amount).toBeUndefined()
  })

  it('accepts a number in an optional field', () => {
    expect(formOf(false).parse({ amount: 0 }).amount).toBe(0)
  })

  it.each([undefined, '', null, Number.NaN])('refuses a blank required number (%s) as required', value => {
    const result = formOf(true).safeParse({ amount: value })

    expect(result.success).toBe(false)
    expect(result.error?.issues[0]?.message).toBe('Required')
  })

  it('accepts a number in a required field', () => {
    expect(formOf(true).parse({ amount: 42 }).amount).toBe(42)
  })

  it('refuses text that is not a number', () => {
    expect(formOf(false).safeParse({ amount: 'abc' }).success).toBe(false)
  })
})

describe('defaultVariableValues', () => {
  it('starts a number with no default empty instead of as an empty string', () => {
    const values = defaultVariableValues([
      variable('Income', WorkflowVariableTypes.Number),
      variable('Instalment', WorkflowVariableTypes.Number, { isRequired: true }),
    ])

    expect(values).toEqual({ Income: undefined, Instalment: undefined })
  })

  it('applies each type of default', () => {
    const values = defaultVariableValues([
      variable('Rate', WorkflowVariableTypes.Number, { defaultValue: '2.5' }),
      variable('Zero', WorkflowVariableTypes.Number, { defaultValue: '0' }),
      variable('Name', WorkflowVariableTypes.String, { defaultValue: 'Acme' }),
      variable('Asof', WorkflowVariableTypes.DateTime, { defaultValue: '2026-03-31T00:00:00' }),
      variable('Tags', WorkflowVariableTypes.MultiSelect, { defaultValue: 'a|b' }),
    ])

    expect(values).toEqual({ Rate: 2.5, Zero: 0, Name: 'Acme', Asof: '2026-03-31', Tags: ['a', 'b'] })
  })

  it('starts other types with no default as an empty string or an empty list', () => {
    const values = defaultVariableValues([
      variable('Name', WorkflowVariableTypes.String),
      variable('Choice', WorkflowVariableTypes.Dropdown),
      variable('Asof', WorkflowVariableTypes.DateTime),
      variable('Items', WorkflowVariableTypes.List),
      variable('Tags', WorkflowVariableTypes.MultiSelect),
    ])

    expect(values).toEqual({ Name: '', Choice: '', Asof: '', Items: [], Tags: [] })
  })
})

describe('submittableVariables', () => {
  const definitions = [
    variable('Choice', WorkflowVariableTypes.Dropdown, { isRequired: true }),
    variable('Income', WorkflowVariableTypes.Number),
    variable('Rate', WorkflowVariableTypes.Number, { defaultValue: '2' }),
    variable('Note', WorkflowVariableTypes.String),
    variable('Region', WorkflowVariableTypes.String, { defaultValue: 'EMEA' }),
    variable('Asof', WorkflowVariableTypes.DateTime),
    variable('Items', WorkflowVariableTypes.List),
  ]

  it('leaves blank optional variables out of the request', () => {
    const submitted = submittableVariables(
      { Choice: 'a', Income: undefined, Rate: Number.NaN, Note: '  ', Asof: '', Items: [] },
      definitions,
    )

    expect(submitted).toEqual({ Choice: 'a', Items: [] })
  })

  it('keeps optional variables that have a value, including zero', () => {
    const submitted = submittableVariables(
      { Choice: 'a', Income: 0, Rate: 1.5, Note: 'x', Asof: '2026-01-01' },
      definitions,
    )

    expect(submitted).toEqual({ Choice: 'a', Income: 0, Rate: 1.5, Note: 'x', Asof: '2026-01-01' })
  })

  it('leaves a hidden variable out, so the run takes the template default the summary no longer carries', () => {
    const hidden = [variable('Secret', WorkflowVariableTypes.String, { isRequired: true, isHidden: true })]

    expect(submittableVariables({ Secret: '' }, hidden)).toEqual({})
  })

  it('sends a blank text field that has a default, so the blank overrides the default', () => {
    expect(submittableVariables({ Region: '' }, definitions)).toEqual({ Region: '' })
  })

  it('keeps a blank required variable, so the API reports it', () => {
    expect(submittableVariables({ Choice: '' }, definitions)).toEqual({ Choice: '' })
  })

  it('keeps a value whose variable it does not know', () => {
    expect(submittableVariables({ Other: '' }, definitions)).toEqual({ Other: '' })
  })
})

describe('appliedTemplateDefault', () => {
  it('names the default a left-out number gets from the template', () => {
    expect(appliedTemplateDefault(variable('Rate', WorkflowVariableTypes.Number, { defaultValue: '2.50' }), '')).toBe('2.5')
  })

  it('names the day a left-out date gets from the template', () => {
    const asof = variable('Asof', WorkflowVariableTypes.DateTime, { defaultValue: '2026-03-31T00:00:00' })

    expect(appliedTemplateDefault(asof, '')).toBe('2026-03-31')
  })

  it('names nothing when the field has a value, has no default, or is sent blank', () => {
    expect(appliedTemplateDefault(variable('Rate', WorkflowVariableTypes.Number, { defaultValue: '2' }), 3)).toBeUndefined()
    expect(appliedTemplateDefault(variable('Income', WorkflowVariableTypes.Number), undefined)).toBeUndefined()
    expect(appliedTemplateDefault(variable('Region', WorkflowVariableTypes.String, { defaultValue: 'EMEA' }), '')).toBeUndefined()
  })
})
