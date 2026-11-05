import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import type { Ref } from 'vue'

import { EN } from '@/assets/locales/en'
import CreateWorkflow from './CreateWorkflow.vue'
import useCreateWorkflowPrefillStore from '@/store/createWorkflowPrefill'

const { createExecution } = vi.hoisted(() => ({ createExecution: vi.fn() }))

vi.mock('@/api', async () => {
  const { ref: vueRef, computed: vueComputed } = await vi.importActual<typeof import('vue')>('vue')
  const variablesOf = (typeId: string) =>
    typeId === 'credit-memo'
      ? [
          { replacementTag: 'CompanyName', displayName: 'Company name', type: 'String', isRequired: true, options: {} },
          {
            replacementTag: 'account_holder_type',
            displayName: 'Account Holder',
            type: 'Dropdown',
            isRequired: true,
            options: { Individual: 'Individual', Company: 'Company' },
          },
          { replacementTag: 'ApiKey', displayName: 'API key', type: 'String', isRequired: false, defaultValue: 'template-default', options: {} },
        ]
      : typeId === 'loan-check'
        ? [
            {
              replacementTag: 'Segment',
              displayName: 'Segment',
              type: 'Dropdown',
              isRequired: true,
              options: { Retail: 'Retail', Sme: 'SME' },
            },
            { replacementTag: 'Income', displayName: 'Declared income', type: 'Number', isRequired: false, options: {} },
            { replacementTag: 'Months', displayName: 'Months', type: 'Number', isRequired: false, defaultValue: '12', options: {} },
            { replacementTag: 'Note', displayName: 'Note', type: 'String', isRequired: false, options: {} },
            { replacementTag: 'Region', displayName: 'Region', type: 'String', isRequired: false, defaultValue: 'EMEA', options: {} },
            { replacementTag: 'Limit', displayName: 'Limit', type: 'Number', isRequired: true, options: {} },
          ]
        : []
  const workflow = (id: string, workflowTypeName: string) => ({ id, workflowTypeId: id, workflowTypeName, isAvailable: true })
  return {
    default: {
      WorkflowCategories: {
        useGetWorkflowCategorySummaries: () => ({
          data: vueRef([
            { id: 'monitoring', name: 'Monitoring', workflows: [workflow('hf-monthly', 'Hedge Fund Monthly'), workflow('pe-quarterly', 'PE Quarterly')] },
            { id: 'credit', name: 'Credit', workflows: [workflow('credit-memo', 'Credit Memo'), workflow('loan-check', 'Loan Check'), workflow('company-report', 'Company Report')] },
          ]),
          isError: vueRef(false),
          refetch: vi.fn(),
        }),
      },
      Workflows: {
        useGetWorkflowSummaryByType: (typeId: Ref<string | null>) => ({
          data: vueComputed(() =>
            typeId.value
              ? {
                  workflowTypeId: typeId.value,
                  isAvailable: true,
                  isCounterpartyRequired: typeId.value === 'company-report',
                  documentClasses: [],
                  variables: variablesOf(typeId.value),
                }
              : null,
          ),
          isError: vueRef(false),
          isLoading: vueRef(false),
          refetch: vi.fn(),
        }),
      },
      WorkflowExecutions: {
        useCreateWorkflowExecution: () => ({ mutateAsync: createExecution, isPending: vueRef(false) }),
      },
      Counterparties: {
        useGetCounterparties: () => ({ data: vueRef(null) }),
      },
      CounterpartyTypes: {
        useGetCounterpartyTypes: () => ({ data: vueRef(null), isLoading: vueRef(false) }),
      },
      Files: (await import('@/test/helpers/uploaderMocks')).apiModule.default.Files,
      PeerCandidates: {
        useGetPeerCandidates: () => ({ data: vueRef(null), isError: vueRef(false) }),
      },
    },
  }
})

vi.mock('vue-router', async importOriginal => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}))

let wrapper: VueWrapper | null = null

beforeEach(() => {
  setActivePinia(createPinia())
  createExecution.mockReset()
})

afterEach(() => {
  wrapper?.unmount()
  wrapper = null
})

const mountPage = async () => {
  wrapper = mount(CreateWorkflow, {
    attachTo: document.body,
    global: {
      plugins: [createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false })],
      stubs: { 'font-awesome-icon': true, CounterpartySearchableSelect: true, PeerCompanyMultiSelect: true },
    },
  })
  await flushPromises()
  return wrapper
}

const chooseType = async (id: string) => {
  await wrapper!.get(`input[type="radio"][value="${id}"]`).setValue(true)
  await flushPromises()
}

const press = async (name: string) => {
  const button = wrapper!.findAll('button').find(candidate => candidate.text() === name)
  if (!button) throw new Error(`no button named ${name}`)
  await button.trigger('click')
  await flushPromises()
}

const openDocumentsStep = async () => {
  await mountPage()
  await chooseType('credit-memo')
  await press('Continue')
}

const stepItems = () => wrapper!.get('ol.step-rail').findAll('li')

describe('CreateWorkflow step list', () => {
  it('is named, so a screen reader announces it as the list of steps', async () => {
    await mountPage()

    expect(wrapper!.get('ol.step-rail').attributes('aria-label')).toBe('Steps')
  })

  it('keeps a completed step numbered and says it is complete', async () => {
    await openDocumentsStep()

    const completed = stepItems()[0]!.get('button')
    expect(completed.get('.step-rail-marker').text()).toBe('1')
    expect(completed.text()).toContain('Report type')
    expect(completed.text()).toContain('completed')
    expect(stepItems()[1]!.text()).not.toContain('completed')
  })

  it('marks the step the user is on as current, and offers no disabled button for it', async () => {
    await openDocumentsStep()

    const current = wrapper!.get('ol.step-rail [aria-current="step"]')
    expect(current.text()).toContain('2')
    expect(current.text()).toContain('Documents')
    expect(current.findAll('button')).toHaveLength(0)
    expect(stepItems().filter(item => item.attributes('aria-current'))).toHaveLength(1)
  })

  it('takes the user back to a completed step from the list', async () => {
    await openDocumentsStep()

    await stepItems()[0]!.get('button').trigger('click')
    await flushPromises()

    expect(stepItems()[0]!.attributes('aria-current')).toBe('step')
  })
})

describe('CreateWorkflow report type step', () => {
  it('groups every report type radio in one radiogroup named by the step heading', async () => {
    await mountPage()

    const group = wrapper!.get('[role="radiogroup"]')
    const heading = document.getElementById(group.attributes('aria-labelledby')!)

    expect(heading?.tagName).toBe('H2')
    expect(heading?.textContent).toBe('Choose a report type')
    expect(group.findAll('input[type="radio"]').map(radio => radio.attributes('value'))).toEqual([
      'hf-monthly',
      'pe-quarterly',
      'credit-memo',
      'loan-check',
      'company-report',
    ])
    expect(wrapper!.findAll('input[type="radio"]')).toHaveLength(5)
  })

  it('still selects a report type when its radio is chosen', async () => {
    await mountPage()

    await wrapper!.get('input[type="radio"][value="credit-memo"]').setValue(true)
    await flushPromises()

    expect(wrapper!.get('label.type-card.is-selected').text()).toContain('Credit Memo')
  })

  it('keeps the chosen report type checked after Continue and Back', async () => {
    await openDocumentsStep()
    await press('Back')

    const checked = wrapper!
      .findAll('input[type="radio"]')
      .filter(radio => (radio.element as HTMLInputElement).checked)
      .map(radio => radio.attributes('value'))
    expect(checked).toEqual(['credit-memo'])
    expect(wrapper!.get('label.type-card.is-selected').text()).toContain('Credit Memo')
  })
})

describe('CreateWorkflow after a re-run found the workflow changed', () => {
  const prefillReRun = (extraNeeded: { replacementTag: string; displayName: string; reason: 'NoValue' | 'InvalidValue' | 'Confidential'; message: string }[] = []) =>
    useCreateWorkflowPrefillStore().set({
      workflowId: 'credit-memo',
      workflowTypeId: 'credit-memo',
      variables: { CompanyName: 'Acme Ltd' },
      documents: [],
      startStep: 'details',
      reRunChanges: {
        variablesNeedingInput: [
          {
            replacementTag: 'account_holder_type',
            displayName: 'Account Holder',
            reason: 'NoValue',
            message: "Required variable 'Account Holder' (account_holder_type) is missing.",
          },
          ...extraNeeded,
        ],
        documentClassesNeedingInput: [],
        removedVariables: [{ replacementTag: 'Region', displayName: 'Region' }],
        defaultedVariables: [],
        unavailableDocuments: [],
      },
    })

  it('opens on the details step with the original value, the note, and the new field marked', async () => {
    prefillReRun()
    await mountPage()

    expect(wrapper!.get('[aria-current="step"]').text()).toContain('Details')
    const note = wrapper!.get('[data-testid="rerun-changes"]')
    expect(note.attributes('role')).toBe('note')
    expect(note.text()).toContain('This workflow has changed since the original run')
    expect(note.text()).toContain('Needs your input: Account Holder')
    expect(note.text()).toContain('Removed from the workflow, so left out: Region')
    expect(wrapper!.findAll('[data-rerun-field]').map(field => field.attributes('data-rerun-field'))).toEqual([
      'account_holder_type',
    ])
    expect(wrapper!.get('[data-rerun-field]').text()).toContain('Needs a value')
    expect((wrapper!.get('input[name="variables.CompanyName"]').element as HTMLInputElement).value).toBe('Acme Ltd')
  })

  it('drops the note when the user picks a different report type', async () => {
    prefillReRun()
    await mountPage()

    await stepItems()[0]!.get('button').trigger('click')
    await flushPromises()
    await chooseType('hf-monthly')

    expect(wrapper!.find('[data-testid="rerun-changes"]').exists()).toBe(false)
  })

  it('keeps a value to enter again blank instead of filling it with the template default', async () => {
    prefillReRun([
      {
        replacementTag: 'ApiKey',
        displayName: 'API key',
        reason: 'Confidential',
        message: "Confidential variable 'API key' (ApiKey) is not carried over to a re-run.",
      },
    ])
    await mountPage()

    expect((wrapper!.get('input[name="variables.ApiKey"]').element as HTMLInputElement).value).toBe('')
    expect(wrapper!.get('[data-rerun-field="ApiKey"]').text()).toContain('Enter again')
    expect(wrapper!.get('[data-testid="rerun-changes"]').text()).toContain('Needs your input: Account Holder, API key')
  })
})

describe('CreateWorkflow optional number fields', () => {
  const variableInput = (tag: string) => wrapper!.get(`input[name="variables.${tag}"]`)

  const fieldError = (tag: string) =>
    (variableInput(tag).attributes('aria-describedby') ?? '')
      .split(/\s+/)
      .filter(Boolean)
      .map(id => document.getElementById(id)?.textContent ?? '')
      .join(' ')

  const openDetailsStep = async () => {
    await mountPage()
    await chooseType('loan-check')
    await press('Continue')
    await press('Continue')
  }

  const chooseSegment = async () => {
    wrapper!.findComponent({ name: 'IniaSelect' }).vm.$emit('update:modelValue', 'Retail')
    await flushPromises()
  }

  const currentStepText = () => wrapper!.get('[aria-current="step"]').text()

  const submit = async () => {
    await press('Continue')
    await vi.waitFor(() => expect(currentStepText()).toContain('Review'))
    await press('Submit')
    await vi.waitFor(() => expect(createExecution).toHaveBeenCalled())
  }

  it('starts an optional number with no default blank and one with a default filled', async () => {
    await openDetailsStep()

    expect((variableInput('Income').element as HTMLInputElement).value).toBe('')
    expect((variableInput('Months').element as HTMLInputElement).value).toBe('12')
  })

  it('submits with optional fields left blank and leaves them out of the request', async () => {
    await openDetailsStep()
    await chooseSegment()
    await variableInput('Limit').setValue('5000')

    await submit()

    expect(createExecution).toHaveBeenCalledTimes(1)
    expect(createExecution.mock.calls[0]![0].variables).toEqual({
      Segment: 'Retail',
      Months: 12,
      Region: 'EMEA',
      Limit: 5000,
    })
  })

  it('leaves a cleared optional number out, and says on review that the template default applies', async () => {
    await openDetailsStep()
    await chooseSegment()
    await variableInput('Limit').setValue('5000')
    await variableInput('Months').setValue('')

    await press('Continue')
    await vi.waitFor(() => expect(currentStepText()).toContain('Review'))
    expect(wrapper!.get('.review-list').text()).toContain('12 (template default)')
    await press('Submit')
    await vi.waitFor(() => expect(createExecution).toHaveBeenCalled())

    expect(createExecution.mock.calls[0]![0].variables).toEqual({ Segment: 'Retail', Region: 'EMEA', Limit: 5000 })
  })

  it('still sends a cleared optional text field with a default as empty, so it overrides the default', async () => {
    await openDetailsStep()
    await chooseSegment()
    await variableInput('Limit').setValue('5000')
    await variableInput('Region').setValue('')

    await submit()

    expect(createExecution.mock.calls[0]![0].variables).toEqual({
      Segment: 'Retail',
      Months: 12,
      Region: '',
      Limit: 5000,
    })
  })

  it('submits a re-run whose earlier values left an optional number blank', async () => {
    useCreateWorkflowPrefillStore().set({
      workflowId: 'loan-check',
      workflowTypeId: 'loan-check',
      variables: { Segment: 'Retail', Limit: 5000, Income: '', Months: null, Note: '', Region: 'APAC' },
      documents: [],
      startStep: 'details',
    })
    await mountPage()

    await submit()

    expect(createExecution.mock.calls[0]![0].variables).toEqual({ Segment: 'Retail', Region: 'APAC', Limit: 5000 })
  })

  it('still holds the user on the details step while a required number is blank', async () => {
    await openDetailsStep()
    await chooseSegment()

    await press('Continue')

    await vi.waitFor(() => expect(fieldError('Limit')).toBe('Required'))
    expect(currentStepText()).toContain('Details')
    expect(fieldError('Income')).toBe('')
    expect(createExecution).not.toHaveBeenCalled()
  })
})

describe('CreateWorkflow company field', () => {
  it('says to choose a company when a report type needs one and none is chosen', async () => {
    await mountPage()
    await chooseType('company-report')
    await press('Continue')
    await press('Continue')

    await press('Continue')

    await vi.waitFor(() => expect(wrapper!.text()).toContain('Choose a company for this report.'))
    expect(wrapper!.text()).not.toContain('expected string')
    expect(createExecution).not.toHaveBeenCalled()
  })
})
