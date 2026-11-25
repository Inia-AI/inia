import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { defineComponent, h, ref, type Ref } from 'vue'
import { extensionPoints } from '@/modules/extensionPoints'

import { EN } from '@/assets/locales/en'
import CreateWorkflow from './CreateWorkflow.vue'
import useCreateWorkflowPrefillStore from '@/store/createWorkflowPrefill'

const { createExecution } = vi.hoisted(() => ({ createExecution: vi.fn() }))

const classifier = vi.hoisted(() => ({ classesToChoose: [] as string[] }))

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
            { replacementTag: 'Weighting', displayName: 'Weighting', type: 'Number', isRequired: false, isHidden: true, defaultValue: '3', options: {} },
          ]
        : typeId === 'interim-report'
          ? [
              {
                replacementTag: 'InterimPeriod',
                displayName: '2. Interim Period',
                type: 'Dropdown',
                isRequired: true,
                defaultValue: 'NA',
                options: { NA: 'NA', '6': '6 months' },
                inputRules: [
                  {
                    conditions: [{ operator: 'AnyAttached', documentClassKeys: ['QuarterlyReport'] }],
                    requirement: { operator: 'NotEqualTo', value: 'NA' },
                  },
                  {
                    conditions: [{ operator: 'NoneAttached', documentClassKeys: ['QuarterlyReport'] }],
                    requirement: { operator: 'EqualTo', value: 'NA' },
                  },
                ],
              },
              {
                replacementTag: 'Reviewer',
                displayName: 'Reviewer',
                type: 'String',
                isRequired: false,
                isHidden: true,
                options: {},
                inputRules: [
                  {
                    conditions: [{ operator: 'AnyAttached', documentClassKeys: ['QuarterlyReport'] }],
                    requirement: { operator: 'NotEmpty' },
                  },
                ],
              },
              {
                replacementTag: 'Comment',
                displayName: 'Comment',
                type: 'String',
                isRequired: false,
                options: {},
                inputRules: [
                  {
                    conditions: [{ operator: 'IsEmpty', variable: 'reviewer' }],
                    requirement: { operator: 'NotEmpty' },
                  },
                ],
              },
            ]
          : []
  const documentClassesOf = (typeId: string) =>
    typeId === 'interim-report'
      ? [{ key: 'QuarterlyReport', displayName: 'Quarterly Report', description: '', requirement: 'Optional', shouldBeSingular: false, isOutputDocument: false }]
      : []
  const workflow = (id: string, workflowTypeName: string) => ({ id, workflowTypeId: id, workflowTypeName, isAvailable: true })
  return {
    default: {
      WorkflowCategories: {
        useGetWorkflowCategorySummaries: () => ({
          data: vueRef([
            { id: 'monitoring', name: 'Monitoring', workflows: [workflow('hf-monthly', 'Hedge Fund Monthly'), workflow('pe-quarterly', 'PE Quarterly')] },
            { id: 'credit', name: 'Credit', workflows: [workflow('credit-memo', 'Credit Memo'), workflow('loan-check', 'Loan Check'), workflow('company-report', 'Company Report'), workflow('interim-report', 'Interim Report')] },
            { id: 'funds', name: 'Funds', workflows: [workflow('fund-report', 'Fund Report'), workflow('fund-report-classified', 'Fund Report Classified')] },
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
                  classifiesDocuments: typeId.value === 'fund-report-classified',
                  classesToChoose: typeId.value === 'fund-report-classified' ? classifier.classesToChoose : undefined,
                  documentClasses: typeId.value.startsWith('fund-report')
                    ? [
                        { key: 'Prospectus', displayName: 'Prospectus', requirement: 'Optional', shouldBeSingular: typeId.value === 'fund-report-classified', isOutputDocument: false },
                        { key: 'AnnualReport', displayName: 'Annual Report', requirement: classifier.classesToChoose.includes('AnnualReport') ? 'Required' : 'Optional', isOutputDocument: false },
                        { key: 'Analysis', displayName: 'Analysis', requirement: 'Optional', isOutputDocument: true },
                      ]
                    : documentClassesOf(typeId.value),
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
  classifier.classesToChoose = []
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
      'interim-report',
      'fund-report',
      'fund-report-classified',
    ])
    expect(wrapper!.findAll('input[type="radio"]')).toHaveLength(8)
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

describe('CreateWorkflow left for another site and back, such as a payment page', () => {
  const leaveThePage = () => window.dispatchEvent(new Event('pagehide'))

  beforeEach(() => sessionStorage.clear())

  it('keeps the chosen type, the values and the documents, and opens on the step it was left on', async () => {
    useCreateWorkflowPrefillStore().set({
      workflowId: 'credit-memo',
      workflowTypeId: 'credit-memo',
      variables: { CompanyName: 'Acme Ltd' },
      documents: [{ fileId: 'file-1', documentClass: 'Annual Report', name: 'annual-report', extension: '.pdf' }],
      startStep: 'details',
    })
    await mountPage()
    expect(wrapper!.get('[aria-current="step"]').text()).toContain('Details')

    leaveThePage()
    wrapper!.unmount()
    await mountPage()

    expect(wrapper!.get('[aria-current="step"]').text()).toContain('Details')
    expect((wrapper!.get('input[name="variables.CompanyName"]').element as HTMLInputElement).value).toBe('Acme Ltd')
    expect(wrapper!.find('[data-testid="rerun-changes"]').exists(), 'a kept form is not a re-run').toBe(false)
    await press('Back')
    expect(wrapper!.text()).toContain('annual-report.pdf')
  })

  it('uses a kept form once, so the next visit starts empty', async () => {
    useCreateWorkflowPrefillStore().set({ workflowId: 'credit-memo', workflowTypeId: 'credit-memo', variables: {}, documents: [], startStep: 'details' })
    await mountPage()
    leaveThePage()
    wrapper!.unmount()
    await mountPage()
    wrapper!.unmount()

    await mountPage()

    expect(wrapper!.get('[aria-current="step"]').text()).toContain('Report type')
  })

  it('keeps nothing while no report type is chosen', async () => {
    await mountPage()

    leaveThePage()

    expect(sessionStorage.length).toBe(0)
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

describe('CreateWorkflow re-run whose value breaks a rule the workflow gained', () => {
  it('shows the message the server gave under the field to enter again', async () => {
    const message = '"Company name" cannot be "Unknown" because "Annual Report" is attached.'
    useCreateWorkflowPrefillStore().set({
      workflowId: 'credit-memo',
      workflowTypeId: 'credit-memo',
      variables: { CompanyName: 'Unknown', account_holder_type: 'Company' },
      documents: [],
      startStep: 'details',
      reRunChanges: {
        variablesNeedingInput: [{ replacementTag: 'CompanyName', displayName: 'Company name', reason: 'InvalidValue', message }],
        documentClassesNeedingInput: [],
        removedVariables: [],
        defaultedVariables: [],
        unavailableDocuments: [],
      },
    })
    await mountPage()

    const notes = wrapper!.get('[data-rerun-field="CompanyName"] [data-testid="field-rule-messages"]')
    expect(notes.text()).toBe(message)
    expect(wrapper!.get('input[name="variables.CompanyName"]').attributes('aria-describedby')).toContain(notes.attributes('id'))
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

  it('never shows or sends a hidden variable, not even one a re-run carries over', async () => {
    useCreateWorkflowPrefillStore().set({
      workflowId: 'loan-check',
      workflowTypeId: 'loan-check',
      variables: { Segment: 'Retail', Limit: 5000, Region: 'APAC', Weighting: 7 },
      documents: [],
      startStep: 'details',
    })
    await mountPage()

    expect(wrapper!.find('input[name="variables.Weighting"]').exists()).toBe(false)
    await submit()

    expect(createExecution.mock.calls[0]![0].variables).not.toHaveProperty('Weighting')
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

describe('CreateWorkflow refused submission', () => {
  const currentStepText = () => wrapper!.get('[aria-current="step"]').text()

  const submitRefusedWith = async (data: object) => {
    createExecution.mockRejectedValue({ response: { status: 400, data } })
    await mountPage()
    await chooseType('hf-monthly')
    await press('Continue')
    await press('Continue')
    await press('Continue')
    await vi.waitFor(() => expect(currentStepText()).toContain('Review'))
    await press('Submit')
    await vi.waitFor(() => expect(createExecution).toHaveBeenCalled())
    await flushPromises()
  }

  it.each([
    ['Run.NotEnoughCredits', 'This report costs 2 credits and you have 1 left. Subscribe to get more credits.'],
    ['Run.Paused', 'New reports are paused because 3 reports failed today. Support will look at them and tell you when you can run reports again.'],
    ['Run.NotOffered', 'This report is not available on your account. Choose a report from the list.'],
  ])('keeps the user on the review step and shows a %s refusal there', async (code, message) => {
    await submitRefusedWith({ title: 'One or more validation errors occurred.', status: 400, errors: { [code]: [message] } })

    expect(currentStepText()).toContain('Review')
    expect(wrapper!.get('[data-testid="submit-refusal"]').text()).toBe(message)
    expect(wrapper!.get('[data-testid="submit-refusal"]').attributes('role')).toBe('alert')
  })

  it('shows a refusal without a validation map on the review step', async () => {
    await submitRefusedWith({ title: 'Not included', status: 403, detail: 'Starting reports is not included in your account.', capability: 'runs' })

    expect(currentStepText()).toContain('Review')
    expect(wrapper!.get('[data-testid="submit-refusal"]').text()).toBe('Starting reports is not included in your account.')
  })

  it('still sends a refusal about the documents back to the documents step', async () => {
    await submitRefusedWith({ errors: { 'Run.TooManyDocuments': ['This report reads at most 2 documents, and 3 are attached. Remove 1, then start it again.'] } })

    expect(currentStepText()).toContain('Documents')
    expect(wrapper!.text()).toContain('This report reads at most 2 documents, and 3 are attached.')
    expect(wrapper!.find('[data-testid="submit-refusal"]').exists()).toBe(false)
  })
})

describe('CreateWorkflow documents step when a module has the customer name every document', () => {
  const asksForAClass = () => ref(true)
  const continueButton = () => wrapper!.findAll('button').find(candidate => candidate.text() === 'Continue')!

  const openDocumentsStepWith = async (workflowTypeId: string, documentClass?: string) => {
    useCreateWorkflowPrefillStore().set({
      workflowId: workflowTypeId,
      workflowTypeId,
      variables: {},
      documents: [{ fileId: 'file-1', documentClass, name: 'fund-prospectus', extension: '.pdf' }],
      startStep: 'details',
    })
    await mountPage()
    await press('Back')
  }

  beforeEach(() => {
    sessionStorage.clear()
    extensionPoints.documentsNeedAClass.push(asksForAClass)
  })

  afterEach(() => {
    extensionPoints.documentsNeedAClass.splice(extensionPoints.documentsNeedAClass.indexOf(asksForAClass), 1)
  })

  it('names the accepted document types without a click and says to label each file', async () => {
    await openDocumentsStepWith('fund-report')

    expect(wrapper!.get('h2.step-title').text()).toBe('Upload and label your documents')
    expect(wrapper!.get('[data-testid="accepted-document-types"]').text()).toBe(
      'Accepted document types: Prospectus, Annual Report. Other document types are not supported.',
    )
    expect(wrapper!.find('.doc-req-toggle').exists(), 'the collapsed Optional row is replaced by the list').toBe(false)
    expect(wrapper!.text()).toContain('Step 1: Drag and drop your files here, or')
    expect(wrapper!.get('[data-testid="choose-class-step"]').text()).toBe('Step 2: Select a document type for each file')
  })

  it('holds Continue while a file has no document type, and says why', async () => {
    await openDocumentsStepWith('fund-report')

    expect(continueButton().attributes('disabled')).toBeDefined()
    expect(wrapper!.get('[data-testid="documents-without-a-class"]').text()).toBe('Select a document type for each file to continue.')
    expect(wrapper!.text()).toContain('Select document type')
    expect(wrapper!.find('.file-class-field.is-unchosen').exists()).toBe(true)
  })

  it('lets the customer continue once every file has a document type', async () => {
    await openDocumentsStepWith('fund-report', 'Prospectus')

    expect(continueButton().attributes('disabled')).toBeUndefined()
    expect(wrapper!.find('[data-testid="documents-without-a-class"]').exists()).toBe(false)
    expect(wrapper!.find('.file-class-field.is-unchosen').exists()).toBe(false)
  })

  it('leaves a report type whose documents the platform classifies as it was', async () => {
    await openDocumentsStepWith('fund-report-classified')

    expect(wrapper!.get('h2.step-title').text()).toBe('Upload documents')
    expect(wrapper!.find('[data-testid="accepted-document-types"]').exists()).toBe(false)
    expect(continueButton().attributes('disabled')).toBeUndefined()
  })

  it('asks for no document type when the platform classifies every document', async () => {
    await openDocumentsStepWith('fund-report-classified')

    expect(wrapper!.find('.file-class-field').exists()).toBe(false)
    expect(continueButton().attributes('disabled')).toBeUndefined()
  })

  it('lets two files carried with the same single-file type continue when the platform classifies every document', async () => {
    useCreateWorkflowPrefillStore().set({
      workflowId: 'fund-report-classified',
      workflowTypeId: 'fund-report-classified',
      variables: {},
      documents: [
        { fileId: 'file-1', documentClass: 'Prospectus', name: 'prospectus-2025', extension: '.pdf' },
        { fileId: 'file-2', documentClass: 'Prospectus', name: 'prospectus-2024', extension: '.pdf' },
      ],
      startStep: 'details',
    })
    await mountPage()
    await press('Back')

    expect(continueButton().attributes('disabled')).toBeUndefined()
  })

  it('sends no input document type the customer cannot see, and keeps an output document type', async () => {
    useCreateWorkflowPrefillStore().set({
      workflowId: 'fund-report-classified',
      workflowTypeId: 'fund-report-classified',
      variables: {},
      documents: [
        { fileId: 'file-1', documentClass: 'AnnualReport', name: 'annual-report', extension: '.pdf' },
        { fileId: 'file-2', documentClass: 'Analysis', name: 'analysis', extension: '.xlsx' },
      ],
      startStep: 'details',
    })
    await mountPage()
    await press('Continue')
    await vi.waitFor(() => expect(wrapper!.get('[aria-current="step"]').text()).toContain('Review'))

    expect(wrapper!.findAll('.review-doc-class').map(documentClass => documentClass.text())).toEqual(['We decide', 'Analysis'])

    await press('Submit')
    await vi.waitFor(() => expect(createExecution).toHaveBeenCalled())
    expect(createExecution.mock.calls[0]![0].files).toEqual([
      expect.objectContaining({ fileId: 'file-1', documentClass: undefined }),
      expect.objectContaining({ fileId: 'file-2', documentClass: 'Analysis' }),
    ])
  })

  it('keeps the document type list when the platform leaves a class for the customer to choose', async () => {
    classifier.classesToChoose = ['AnnualReport']

    await openDocumentsStepWith('fund-report-classified')

    expect(wrapper!.find('.file-class-field').exists()).toBe(true)
  })
})

describe('CreateWorkflow documents step when no module has the customer name every document', () => {
  beforeEach(() => sessionStorage.clear())

  it('keeps the usual wording and lets a file go without a document type', async () => {
    useCreateWorkflowPrefillStore().set({
      workflowId: 'fund-report',
      workflowTypeId: 'fund-report',
      variables: {},
      documents: [{ fileId: 'file-1', documentClass: undefined, name: 'fund-prospectus', extension: '.pdf' }],
      startStep: 'details',
    })
    await mountPage()
    await press('Back')

    expect(wrapper!.get('h2.step-title').text()).toBe('Upload documents')
    expect(wrapper!.find('[data-testid="accepted-document-types"]').exists()).toBe(false)
    expect(wrapper!.find('.doc-req-toggle').exists()).toBe(true)
    expect(wrapper!.findAll('button').find(candidate => candidate.text() === 'Continue')!.attributes('disabled')).toBeUndefined()
  })
})

describe('CreateWorkflow review piece that holds Submit', () => {
  const holdingPiece = defineComponent({
    emits: ['holds'],
    setup: (_, { emit }) => {
      emit('holds', 'This report costs 2 credits and you have 1 left. Subscribe to get more credits.')
      return () => h('div', { class: 'review-row' })
    },
  })

  beforeEach(() => {
    extensionPoints.reviewDetails.push(holdingPiece)
  })

  afterEach(() => {
    extensionPoints.reviewDetails.splice(extensionPoints.reviewDetails.indexOf(holdingPiece), 1)
  })

  it('turns Submit off and shows the reason beside it, so nothing is sent', async () => {
    await mountPage()
    await chooseType('hf-monthly')
    await press('Continue')
    await press('Continue')
    await press('Continue')
    await vi.waitFor(() => expect(wrapper!.get('[aria-current="step"]').text()).toContain('Review'))

    const submit = wrapper!.get('button[type="submit"]')
    expect(submit.attributes('disabled')).toBeDefined()
    expect(wrapper!.get('[data-testid="submit-held"]').text()).toBe('This report costs 2 credits and you have 1 left. Subscribe to get more credits.')
    expect(submit.attributes('aria-describedby')).toBe('submit-held')

    await wrapper!.get('form').trigger('submit')
    await flushPromises()
    expect(createExecution).not.toHaveBeenCalled()
  })
})

describe('CreateWorkflow review piece that names Submit', () => {
  const namingPiece = defineComponent({
    emits: ['submit-label'],
    setup: (_, { emit }) => {
      emit('submit-label', 'Run report · $75')
      return () => h('div', { class: 'review-row' })
    },
  })

  beforeEach(() => {
    extensionPoints.reviewDetails.push(namingPiece)
  })

  afterEach(() => {
    extensionPoints.reviewDetails.splice(extensionPoints.reviewDetails.indexOf(namingPiece), 1)
  })

  it('shows the name the piece gives on Submit, such as the price', async () => {
    await mountPage()
    await chooseType('hf-monthly')
    await press('Continue')
    await press('Continue')
    await press('Continue')
    await vi.waitFor(() => expect(wrapper!.get('[aria-current="step"]').text()).toContain('Review'))

    expect(wrapper!.get('button[type="submit"]').text()).toBe('Run report · $75')
  })
})

describe('CreateWorkflow input rules on the details step', () => {
  const openDetailsWith = async (documentClasses: string[], variables: Record<string, unknown> = {}) => {
    useCreateWorkflowPrefillStore().set({
      workflowId: 'interim-report',
      workflowTypeId: 'interim-report',
      variables,
      documents: documentClasses.map((documentClass, index) => ({ fileId: `file-${index}`, documentClass, name: `document-${index}`, extension: '.pdf' })),
      startStep: 'details',
    })
    await mountPage()
  }

  const continueButton = () => wrapper!.findAll('button').find(candidate => candidate.text() === 'Continue')!

  it('shows the rule message on the field as the step opens, turns Continue off, and keeps the default', async () => {
    await openDetailsWith(['QuarterlyReport'])

    const message = '"Interim Period" cannot be "NA" because "Quarterly Report" is attached.'
    const notes = wrapper!.get('[data-testid="field-rule-messages"]')
    expect(notes.text()).toBe(message)
    const control = wrapper!.get(`[aria-describedby~="${notes.attributes('id')}"]`)
    expect(control.attributes('aria-invalid')).toBe('true')
    expect(control.text()).toContain('NA')
    expect(continueButton().attributes('disabled')).toBeDefined()
    expect(continueButton().attributes('aria-describedby')).toBe('input-rules-held')
    expect(wrapper!.get('#input-rules-held').attributes('role')).toBe('status')
    expect(wrapper!.get('#input-rules-held').text()).toContain('Change the fields marked on this step before you continue.')
  })

  it('refuses an interim period when no quarterly report is attached', async () => {
    await openDetailsWith([], { InterimPeriod: '6' })

    expect(wrapper!.get('[data-testid="field-rule-messages"]').text()).toBe(
      '"Interim Period" must be "NA" because no "Quarterly Report" is attached.',
    )
    expect(continueButton().attributes('disabled')).toBeDefined()
  })

  it('clears the message and turns Continue on when the user changes the value', async () => {
    await openDetailsWith(['QuarterlyReport'])
    expect(continueButton().attributes('disabled')).toBeDefined()

    wrapper!.findComponent({ name: 'IniaSelect' }).vm.$emit('update:modelValue', '6')
    await flushPromises()

    expect(wrapper!.find('[data-testid="field-rule-messages"]').exists()).toBe(false)
    expect(wrapper!.get('#input-rules-held').text()).toBe('')
    expect(continueButton().attributes('disabled')).toBeUndefined()
  })

  it('leaves to the server a rule on a hidden field and a rule that names one, since the form cannot see its default', async () => {
    await openDetailsWith(['QuarterlyReport'], { InterimPeriod: '6' })

    expect(wrapper!.text()).not.toContain('Reviewer')
    expect(wrapper!.text()).not.toContain('Choose a value for')
    expect(continueButton().attributes('disabled')).toBeUndefined()
  })

  it('lets the user continue when the value meets the rules', async () => {
    await openDetailsWith(['QuarterlyReport'], { InterimPeriod: '6' })

    expect(wrapper!.find('[data-testid="field-rule-messages"]').exists()).toBe(false)
    expect(wrapper!.get('#input-rules-held').text()).toBe('')
    expect(continueButton().attributes('disabled')).toBeUndefined()
    expect(continueButton().attributes('aria-describedby')).toBeUndefined()
  })
})
