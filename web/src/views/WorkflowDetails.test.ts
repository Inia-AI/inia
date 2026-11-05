import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { WorkflowState } from '@/enums'

const { executionRef, sectionsRef, dataOutputsRef, logsRef, refetchSectionsMock, exportReportMock, downloadBlobMock, toastMock, roles, i18n } = vi.hoisted(() => ({
  logsRef: { value: null as unknown },
  refetchSectionsMock: vi.fn(),
  roles: { superAdmin: false },
  executionRef: { value: null as unknown },
  sectionsRef: { value: null as unknown },
  dataOutputsRef: { value: null as unknown },
  exportReportMock: vi.fn(),
  downloadBlobMock: vi.fn(),
  toastMock: vi.fn(),
  i18n: { translate: (key: string) => key },
}))

vi.mock('@/api', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  executionRef.value = vueRef({ id: 'exec-1', workflowState: WorkflowState.Draft })
  sectionsRef.value = vueRef([])
  dataOutputsRef.value = vueRef([])
  logsRef.value = vueRef([])
  const noopQuery = () => ({ data: vueRef(undefined), isLoading: vueRef(false) })
  const noopMutation = () => ({ mutateAsync: vi.fn(), isPending: vueRef(false) })
  return {
    default: {
      WorkflowReports: {
        useGetWorkflowReportSections: () => ({
          data: sectionsRef.value,
          isLoading: vueRef(false),
          isError: vueRef(false),
          refetch: refetchSectionsMock,
        }),
        useGetWorkflowReportData: () => ({ data: dataOutputsRef.value, refetch: vi.fn() }),
        useGetWorkflowReportById: () => ({
          data: executionRef.value,
          isLoading: vueRef(false),
          isError: vueRef(false),
        }),
        useUpdateReportParagraph: noopMutation,
        useExportWorkflowReport: () => ({ mutateAsync: exportReportMock, isPending: vueRef(false) }),
        useDeleteReportParagraph: noopMutation,
        useGetReportFeedback: () => ({ data: vueRef([]), isError: vueRef(false) }),
        useCreateReportFeedback: noopMutation,
        useUpdateReportFeedbackStatus: noopMutation,
        useDeleteReportFeedback: noopMutation,
      },
      Files: { useGetFile: noopMutation },
      WorkflowExecutions: {
        useCreateWorkflowExecution: noopMutation,
        useGetWorkflowExecutionLogs: () => ({ data: logsRef.value, refetch: vi.fn() }),
      },
      Workflows: { useGetWorkflowSummaryByType: noopQuery },
      Users: { useResolveUsers: noopQuery },
    },
  }
})

vi.mock('vue-i18n', async importOriginal => {
  const actual = await importOriginal<typeof import('vue-i18n')>()
  return {
    ...actual,
    useI18n: () => ({ t: (key: string) => i18n.translate(key) }),
  }
})

vi.mock('vue-router', async importOriginal => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return {
    ...actual,
    useRouter: () => ({
      push: vi.fn(),
      currentRoute: { value: { params: { id: 'exec-1' }, query: {} } },
    }),
    useRoute: () => ({ params: { id: 'exec-1' }, query: {} }),
  }
})

vi.mock('@/components/ui/toast/use-toast', () => ({ useToast: () => ({ toast: toastMock }) }))

vi.mock('@/lib/downloadFile', async importOriginal => ({
  ...(await importOriginal<typeof import('@/lib/downloadFile')>()),
  downloadBlob: downloadBlobMock,
}))

vi.mock('@/components/ui/dropdown-menu', async () => {
  const { defineComponent } = await import('vue')
  const passthrough = (name: string) =>
    defineComponent({ name, template: '<div><slot /></div>' })
  return {
    DropdownMenu: passthrough('DropdownMenu'),
    DropdownMenuContent: passthrough('DropdownMenuContent'),
    DropdownMenuTrigger: passthrough('DropdownMenuTrigger'),
    DropdownMenuSeparator: passthrough('DropdownMenuSeparator'),
    DropdownMenuItem: defineComponent({
      name: 'DropdownMenuItem',
      emits: ['select'],
      template: '<button data-stub="menuitem" @click="$emit(\'select\')"><slot /></button>',
    }),
  }
})

vi.mock('@/composables/useCurrentUserRoles', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  return {
    useCurrentUserRoles: () => ({
      isSuperAdmin: vueRef(roles.superAdmin),
      isOperator: vueRef(false),
    }),
  }
})

vi.mock('@/composables/useDownloadExecutedWorkflow', () => ({
  useDownloadExecutedWorkflow: () => ({ download: vi.fn(), isPending: { value: false } }),
}))

vi.mock('@/composables/useCancelExecution', () => ({
  useCancelExecution: () => ({ showDialog: { value: false }, confirm: vi.fn(), prompt: vi.fn() }),
  isCancellableState: () => false,
}))

vi.mock('@/composables/useDeleteReport', () => ({
  useDeleteReport: () => ({
    showDialog: { value: false },
    confirm: vi.fn(),
    prompt: vi.fn(),
    isPending: { value: false },
  }),
}))

vi.mock('@/store/createWorkflowPrefill', () => ({ default: () => ({ set: vi.fn() }) }))

import { defineComponent, inject, ref, type Component } from 'vue'
import { OPEN_REPORT_FEEDBACK_KEY } from '@/components/organisms/ReportFeedback/injectionKeys'
import WorkflowDetails from './WorkflowDetails.vue'
import workflowDetailsMessages from '@/assets/locales/en/page/workflowDetails.json'
import { extensionPoints } from '@/modules/extensionPoints'

const setExecution = (patch: Record<string, unknown>) => {
  ;(executionRef.value as { value: Record<string, unknown> }).value = {
    id: 'exec-1',
    workflowState: WorkflowState.Draft,
    ...patch,
  }
}

const setSections = (sections: unknown[]) => {
  ;(sectionsRef.value as { value: unknown[] }).value = sections
}

const setDataOutputs = (outputs: unknown[]) => {
  ;(dataOutputsRef.value as { value: unknown[] }).value = outputs
}

const factory = ({ stubs = {}, ...options }: { attachTo?: HTMLElement; stubs?: Record<string, Component> } = {}) =>
  mount(WorkflowDetails, {
    ...options,
    global: {
      stubs: {
        WorkflowCredits: true,
        WorkflowElement: true,
        FilePreviewDialog: true,
        CancelExecutionDialog: true,
        DeleteReportDialog: true,
        RunCostDialog: true,
        ReRunChoiceDialog: true,
        ReplayProvenanceBadge: true,
        WorkflowStatusPill: true,
        WorkflowCredits2: true,
        IniaLoader: true,
        IniaIcon: true,
        ModelRefusalNotice: true,
        ...stubs,
      },
    },
  })

describe('WorkflowDetails heading', () => {
  it('names the report by its workflow type when the report carries no title', () => {
    setExecution({ title: '', workflowTypeName: 'QA fixture' })

    const wrapper = factory()
    expect(wrapper.get('h1.page-title').text()).toBe('QA fixture')
    expect(wrapper.find('.workflow-type').exists()).toBe(false)
  })

  it('keeps the report title when it has one', () => {
    setExecution({ title: 'Hedge Fund Monitoring', workflowTypeName: 'Hedge Funds Monitoring' })

    const wrapper = factory()
    expect(wrapper.get('h1.page-title').text()).toBe('Hedge Fund Monitoring')
    expect(wrapper.get('.workflow-type').text()).toBe('Hedge Funds Monitoring')
  })
})

describe('WorkflowDetails classic activity line', () => {
  const workflowActivity = {
    schemaVersion: 1,
    currentStepIndex: 0,
    steps: [{ label: 'Reading the filings' }, { label: 'Extracting the figures' }],
  }

  it('names the current step while the run is live', () => {
    setExecution({ workflowState: WorkflowState.Running, workflowActivity })

    expect(factory().text()).toContain('Reading the filings')
  })

  it('drops the line once the run is no longer running', () => {
    setExecution({ workflowState: WorkflowState.Draft, workflowActivity })

    expect(factory().text()).not.toContain('Reading the filings')
  })
})

describe('WorkflowDetails while the run finishes', () => {
  it('loads the report sections again when the run leaves its running state', async () => {
    setExecution({ workflowState: WorkflowState.Running })
    factory()
    refetchSectionsMock.mockClear()

    setExecution({ workflowState: WorkflowState.Draft })
    await flushPromises()

    expect(refetchSectionsMock).toHaveBeenCalledOnce()
  })
})

describe('WorkflowDetails report export', () => {
  const WORD_LABEL = 'page.workflowDetails.actions.exportWord'
  const MARKDOWN_LABEL = 'page.workflowDetails.actions.exportMarkdown'

  afterEach(() => {
    exportReportMock.mockReset()
    downloadBlobMock.mockReset()
    toastMock.mockReset()
  })

  const clickExport = async (label: string) => {
    const wrapper = factory()
    const item = wrapper
      .findAll('[data-stub="menuitem"]')
      .find(candidate => candidate.text().trim() === label)
    expect(item).toBeTruthy()
    await item!.trigger('click')
    await flushPromises()
  }

  it.each([
    [WORD_LABEL, 'word', 'Credit Review - Acme Holdings - 2026-10-20.docx'],
    [MARKDOWN_LABEL, 'markdown', 'Credit Review - Acme Holdings - 2026-10-20.md'],
  ])('%s exports the %s file and downloads it under the server-sent filename', async (label, format, filename) => {
    exportReportMock.mockResolvedValueOnce({ blob: new Blob(['report']), filename })

    await clickExport(label)

    expect(exportReportMock).toHaveBeenCalledExactlyOnceWith(format)
    expect(downloadBlobMock).toHaveBeenLastCalledWith(expect.any(Blob), filename)
  })

  it.each([
    [WORD_LABEL, 'workflow-exec-1.docx'],
    [MARKDOWN_LABEL, 'workflow-exec-1.md'],
  ])('%s falls back to the id-based name when the server sends none', async (label, fallback) => {
    exportReportMock.mockResolvedValueOnce({ blob: new Blob(['report']), filename: null })

    await clickExport(label)

    expect(downloadBlobMock).toHaveBeenLastCalledWith(expect.any(Blob), fallback)
  })

  it.each([WORD_LABEL, MARKDOWN_LABEL])('%s tells the reader when the export fails', async label => {
    exportReportMock.mockRejectedValueOnce(new Error('export failed'))

    await clickExport(label)

    expect(downloadBlobMock).not.toHaveBeenCalled()
    expect(toastMock).toHaveBeenCalledExactlyOnceWith({
      description: 'page.workflowDetails.actions.exportFailed',
      variant: 'destructive',
    })
  })
})

describe('WorkflowDetails report actions', () => {
  afterEach(() => {
    extensionPoints.reportActions.length = 0
    extensionPoints.reportMenuItems.length = 0
    extensionPoints.reportNotices.length = 0
  })

  it('renders each notice a module registers under the report title', () => {
    extensionPoints.reportNotices.push(defineComponent({ template: '<p data-testid="module-notice">A notice</p>' }))
    setExecution({ isBaseline: false })

    expect(factory().find('[data-testid="module-notice"]').text()).toBe('A notice')
  })

  it('renders each Actions menu item a module registers, with the report', () => {
    extensionPoints.reportMenuItems.push(
      defineComponent({
        props: { report: { type: Object, default: undefined } },
        template: '<span data-testid="module-menu-item">{{ report?.id }}</span>',
      }),
    )
    setExecution({ isBaseline: false })

    expect(factory().find('[data-testid="module-menu-item"]').text()).toBe('exec-1')
  })

  it('shows no baseline star without the validation module', () => {
    setExecution({ isBaseline: true, baselineVerified: true })

    expect(factory().find('[data-testid="baseline-star"]').exists()).toBe(false)
  })

  it('renders each action a module registers, with the report', () => {
    extensionPoints.reportActions.push(
      defineComponent({
        props: { report: { type: Object, default: undefined } },
        template: '<span data-testid="module-action">{{ report?.id }}</span>',
      }),
    )
    setExecution({ isBaseline: false })

    expect(factory().find('[data-testid="module-action"]').text()).toBe('exec-1')
  })
})

describe('WorkflowDetails section navigation', () => {
  afterEach(() => {
    setSections([])
  })

  const TWO_SECTIONS = [
    { id: 's1', title: 'QA FIGURES FROM THE PAGE', paragraphs: [{ id: 'p1', subtitle: 'Revenue' }] },
    { id: 's2', title: 'QA SUMMARY OF THE FIGURES', paragraphs: [{ id: 'p2', subtitle: 'Totals' }] },
  ]

  const mountWithSections = (sections: unknown[] = TWO_SECTIONS) => {
    setExecution({})
    setSections(sections)
    return factory({ attachTo: document.body })
  }

  it('offers every section entry as a keyboard-focusable button in a named navigation', () => {
    const wrapper = mountWithSections()

    const nav = wrapper.get('nav.sidebar-nav')
    expect(nav.attributes('aria-label')).toBe('page.workflowDetails.sectionNav.label')
    const entries = nav.findAll('.nav-section-link')
    expect(entries.map(entry => entry.text())).toEqual(['QA FIGURES FROM THE PAGE', 'QA SUMMARY OF THE FIGURES'])
    for (const entry of entries) {
      expect(entry.element.tagName).toBe('BUTTON')
      expect(entry.attributes('type')).toBe('button')
      ;(entry.element as HTMLButtonElement).focus()
      expect(document.activeElement).toBe(entry.element)
    }
    wrapper.unmount()
  })

  it('marks the shown section and switches to the one the user activates', async () => {
    const wrapper = mountWithSections()
    const entries = () => wrapper.findAll('.nav-section-link')

    expect(entries()[0]!.attributes('aria-current')).toBe('true')
    expect(entries()[1]!.attributes('aria-current')).toBeUndefined()
    expect(wrapper.get('.section-heading').text()).toBe('QA FIGURES FROM THE PAGE')

    await entries()[1]!.trigger('click')

    expect(wrapper.get('.section-heading').text()).toBe('QA SUMMARY OF THE FIGURES')
    expect(entries()[1]!.attributes('aria-current')).toBe('true')
    expect(entries()[0]!.attributes('aria-current')).toBeUndefined()
    wrapper.unmount()
  })

  it('offers every subsection entry as a keyboard-focusable button', () => {
    const wrapper = mountWithSections()

    const subsections = wrapper.findAll('.subsection-link')
    expect(subsections.map(entry => entry.text())).toEqual(['Revenue', 'Totals'])
    for (const entry of subsections) {
      expect(entry.element.tagName).toBe('BUTTON')
      expect(entry.attributes('type')).toBe('button')
    }
    wrapper.unmount()
  })

  it('offers the long-list expand toggle as a button that says whether the list is expanded', async () => {
    const wrapper = mountWithSections(
      Array.from({ length: 6 }, (_, index) => ({ id: `s${index}`, title: `Section ${index}`, paragraphs: [{ id: `p${index}` }] })),
    )

    const toggle = wrapper.get('.nav-expand-toggle')
    expect(toggle.element.tagName).toBe('BUTTON')
    expect(toggle.attributes('type')).toBe('button')
    expect(toggle.attributes('aria-expanded')).toBe('false')
    expect(toggle.text()).toBe('page.workflowDetails.sectionNav.expand')

    await toggle.trigger('click')

    expect(toggle.attributes('aria-expanded')).toBe('true')
    expect(toggle.text()).toBe('page.workflowDetails.sectionNav.collapse')
    wrapper.unmount()
  })
})

describe('WorkflowDetails data outputs', () => {
  afterEach(() => {
    setSections([])
    setDataOutputs([])
  })

  const SECTION = { id: 's1', title: 'Balance Sheet', paragraphs: [{ id: 'p1', subtitle: 'Assets' }] }
  const OUTPUTS = [
    { name: 'facilities', data: { limit: 120000 }, citations: [], citedSourcesByBlock: [] },
    { name: 'financialTool', data: { cash: { FY0: 96410 } }, citations: [], citedSourcesByBlock: [] },
  ]

  const mountWith = (sections: unknown[], outputs: unknown[]) => {
    setExecution({})
    setSections(sections)
    setDataOutputs(outputs)
    return factory({ attachTo: document.body })
  }

  it('lists each data output under a Data entry after the report sections', () => {
    const wrapper = mountWith([SECTION], OUTPUTS)

    const group = wrapper.get('.nav-data-group')
    expect(group.get('.nav-section-link').text()).toBe('page.workflowDetails.dataOutputs.title')
    expect(group.findAll('.subsection-link').map(entry => entry.text())).toEqual(['facilities', 'financialTool'])
    expect(wrapper.get('.section-heading').text()).toBe('Balance Sheet')
    wrapper.unmount()
  })

  it('opens a data output in the section panel, and a section takes the panel back', async () => {
    const wrapper = mountWith([SECTION], OUTPUTS)
    const outputEntry = () => wrapper.get('.nav-data-group').findAll('.subsection-link')[1]!

    await outputEntry().trigger('click')

    expect(wrapper.get('.section-heading').text()).toBe('page.workflowDetails.dataOutputs.title')
    expect(wrapper.get('.data-output__name').text()).toBe('financialTool')
    expect(wrapper.get('.data-output__json').text()).toBe(JSON.stringify(OUTPUTS[1]!.data, null, 2))
    expect(outputEntry().classes()).toContain('subsection-active')
    expect(wrapper.get('.nav-data-group .nav-section-link').attributes('aria-current')).toBe('true')

    await wrapper.findAll('.nav-section-link')[0]!.trigger('click')

    expect(wrapper.get('.section-heading').text()).toBe('Balance Sheet')
    expect(wrapper.find('.data-output').exists()).toBe(false)
    wrapper.unmount()
  })

  it('opens a run that wrote only data on its first data output', () => {
    const wrapper = mountWith([], OUTPUTS)

    expect(wrapper.get('.data-output__name').text()).toBe('facilities')
    wrapper.unmount()
  })

  it('keeps two outputs that share a name both reachable', async () => {
    const twins = [
      { name: 'facility', data: { limit: 1 }, citations: [], citedSourcesByBlock: [] },
      { name: 'facility', data: { limit: 2 }, citations: [], citedSourcesByBlock: [] },
    ]
    const wrapper = mountWith([SECTION], twins)

    await wrapper.get('.nav-data-group').findAll('.subsection-link')[1]!.trigger('click')

    expect(wrapper.get('.data-output__json').text()).toBe(JSON.stringify({ limit: 2 }, null, 2))
    wrapper.unmount()
  })

  it('shows no Data entry for a run without data outputs', () => {
    const wrapper = mountWith([SECTION], [])

    expect(wrapper.find('.nav-data-group').exists()).toBe(false)
    wrapper.unmount()
  })
})

describe('WorkflowDetails exports label', () => {
  const PAGE_KEYS = 'page.workflowDetails.'

  afterEach(() => {
    setSections([])
    i18n.translate = key => key
  })

  it('shows a label that covers every export', () => {
    const messages = workflowDetailsMessages as Record<string, unknown>
    i18n.translate = key => (key.startsWith(PAGE_KEYS) ? (messages[key.slice(PAGE_KEYS.length)] as string | undefined) : undefined) ?? key
    setExecution({})
    setSections([{ id: 's1', title: 'QA DEBUG TRACE', excludeFromWordExport: true, paragraphs: [{ id: 'p1' }] }])

    const label = factory().get('[data-testid="not-in-exports"]')

    expect(label.text()).toBe('Not in exports')
    expect(label.attributes('title')).toBe('This section shows here but is left out of the Word and Markdown exports')
  })

  it('labels the section the exports leave out, and no other section', async () => {
    setExecution({})
    setSections([
      { id: 's1', title: 'QA DEBUG TRACE', excludeFromWordExport: true, paragraphs: [{ id: 'p1' }] },
      { id: 's2', title: 'QA SUMMARY', excludeFromWordExport: false, paragraphs: [{ id: 'p2' }] },
    ])
    const wrapper = factory({ attachTo: document.body })

    expect(wrapper.get('.section-heading').text()).toBe('QA DEBUG TRACE')
    expect(wrapper.get('[data-testid="not-in-exports"]').text()).toBe('page.workflowDetails.notInExports')

    await wrapper.findAll('.nav-section-link')[1]!.trigger('click')

    expect(wrapper.get('.section-heading').text()).toBe('QA SUMMARY')
    expect(wrapper.find('[data-testid="not-in-exports"]').exists()).toBe(false)
    wrapper.unmount()
  })
})

describe('WorkflowDetails feedback', () => {
  const closeMenu = async (wrapper: ReturnType<typeof factory>) => {
    const menuClosing = new Event('focus', { cancelable: true })
    wrapper.findComponent({ name: 'DropdownMenuContent' }).vm.$emit('closeAutoFocus', menuClosing)
    await flushPromises()
    return menuClosing
  }

  const chooseFeedback = async (wrapper: ReturnType<typeof factory>) => {
    await wrapper.get('[data-testid="feedback-menu-item"]').trigger('click')
    return closeMenu(wrapper)
  }

  it('moves focus into the feedback panel when Feedback is chosen from the Actions menu', async () => {
    const wrapper = factory({ attachTo: document.body })

    const menuClosing = await chooseFeedback(wrapper)

    expect(menuClosing.defaultPrevented).toBe(true)
    expect(wrapper.get('[data-testid="report-feedback"]').element.contains(document.activeElement)).toBe(true)
    wrapper.unmount()
  })

  it('returns focus to the Actions button when the panel closes', async () => {
    const wrapper = factory({ attachTo: document.body })
    await chooseFeedback(wrapper)

    const close = wrapper
      .get('[data-testid="report-feedback"]')
      .findAll('button')
      .find(button => button.text() === 'page.workflowDetails.feedback.close')!
    await close.trigger('click')
    await flushPromises()

    expect(document.activeElement?.textContent?.trim()).toBe('Actions')
    wrapper.unmount()
  })

  it('takes focus into the panel from the highlight popup, and gives it to the Actions button once the popup is gone', async () => {
    setExecution({})
    setSections([{ id: 's1', title: 'QA FIGURES', paragraphs: [{ id: 'p1', subtitle: 'Revenue' }] }])
    const HighlightPopup = defineComponent({
      setup: () => {
        const open = inject(OPEN_REPORT_FEEDBACK_KEY)!
        const shown = ref(true)
        const choose = () => {
          open('Revenue was 12m')
          shown.value = false
        }
        return { shown, choose }
      },
      template: '<button v-if="shown" data-testid="highlight-feedback" @click="choose">Feedback</button>',
    })
    const wrapper = factory({ attachTo: document.body, stubs: { WorkflowElement: HighlightPopup } })
    await flushPromises()

    await wrapper.get('[data-testid="highlight-feedback"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="report-feedback"]').element.contains(document.activeElement)).toBe(true)

    const close = wrapper
      .get('[data-testid="report-feedback"]')
      .findAll('button')
      .find(button => button.text() === 'page.workflowDetails.feedback.close')!
    await close.trigger('click')
    await flushPromises()

    expect(document.activeElement?.textContent?.trim()).toBe('Actions')
    setSections([])
    wrapper.unmount()
  })

  it('leaves focus where the menu puts it after any other action', async () => {
    const wrapper = factory({ attachTo: document.body })

    const menuClosing = await closeMenu(wrapper)

    expect(menuClosing.defaultPrevented).toBe(false)
    wrapper.unmount()
  })
})

describe('WorkflowDetails run cost', () => {
  afterEach(() => {
    roles.superAdmin = false
  })

  it('offers the run cost to a super admin, behind a closed dialog', () => {
    roles.superAdmin = true

    const wrapper = factory()

    expect(wrapper.find('[data-testid="run-cost-menu-item"]').exists()).toBe(true)
    const dialog = wrapper.findComponent({ name: 'RunCostDialog' })
    expect(dialog.exists()).toBe(true)
    expect(dialog.attributes('open')).toBe('false')
  })

  it('opens the run cost dialog from the menu', async () => {
    roles.superAdmin = true

    const wrapper = factory()
    await wrapper.get('[data-testid="run-cost-menu-item"]').trigger('click')

    expect(wrapper.findComponent({ name: 'RunCostDialog' }).attributes('open')).toBe('true')
  })

  it('shows no run cost to anyone else', () => {
    const wrapper = factory()

    expect(wrapper.find('[data-testid="run-cost-menu-item"]').exists()).toBe(false)
    expect(wrapper.findComponent({ name: 'RunCostDialog' }).exists()).toBe(false)
  })
})

describe('WorkflowDetails model refusals', () => {
  it('hands the run logs to the refusal notice, so a refused block is named on the report', () => {
    setExecution({ title: 'NomCorporates1' })
    const refused = { id: 'log-1', blockName: 'Extract_Cash', status: 'Failed', refusalCategory: 'reasoning_extraction' }
    ;(logsRef.value as { value: unknown[] }).value = [refused]

    const notice = factory().findComponent({ name: 'ModelRefusalNotice' })

    expect(notice.exists()).toBe(true)
    expect(notice.props('executionId')).toBe('exec-1')
    expect(notice.props('logs')).toEqual([refused])
  })
})
