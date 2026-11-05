import { afterEach, describe, expect, it, vi, beforeEach } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { ref } from 'vue'
import {
  ReportFeedbackCategory,
  ReportFeedbackStatus,
  type ReportFeedbackItem,
} from '@/api/workflowReports/schema'
import { clearAuthToken, setAuthToken } from '@/test/helpers/jwt'
import { formatDateTime } from '@/lib/utils'

const { feedbackRef, createMock, updateMock, deleteMock, toastMock } = vi.hoisted(() => ({
  feedbackRef: { value: null as unknown },
  createMock: vi.fn(),
  updateMock: vi.fn(),
  deleteMock: vi.fn(),
  toastMock: vi.fn(),
}))

vi.mock('@/api', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  return {
    default: {
      WorkflowReports: {
        useGetReportFeedback: () => ({
          data: feedbackRef.value,
          isError: vueRef(false),
        }),
        useCreateReportFeedback: () => ({ mutateAsync: createMock, isPending: vueRef(false) }),
        useUpdateReportFeedbackStatus: () => ({ mutateAsync: updateMock, isPending: vueRef(false) }),
        useDeleteReportFeedback: () => ({ mutateAsync: deleteMock, isPending: vueRef(false) }),
      },
    },
  }
})

vi.mock('vue-i18n', async importOriginal => {
  const actual = await importOriginal<typeof import('vue-i18n')>()
  return {
    ...actual,
    useI18n: () => ({ t: (key: string) => key }),
  }
})

vi.mock('@/components/ui/toast/use-toast', () => ({
  useToast: () => ({ toast: toastMock }),
}))

import ReportFeedbackPanel from './ReportFeedbackPanel.vue'

const item = (overrides: Partial<ReportFeedbackItem> = {}): ReportFeedbackItem => ({
  id: 'fb-1',
  workflowExecutionId: 'exec-1',
  category: ReportFeedbackCategory.WrongFigure,
  comment: 'Revenue should be 125.',
  authorUserId: 'user-1',
  authorDisplayName: 'Grace Hopper',
  status: ReportFeedbackStatus.Open,
  createdAt: '2026-09-09T10:00:00Z',
  ...overrides,
})

const mountPanel = (quotedText = '', options: { attachTo?: HTMLElement } = {}) =>
  mount(ReportFeedbackPanel, {
    props: { executionId: 'exec-1', quotedText, open: true },
    ...options,
  })

beforeEach(() => {
  clearAuthToken()
  createMock.mockReset().mockResolvedValue(item())
  updateMock.mockReset().mockResolvedValue(item({ status: ReportFeedbackStatus.Addressed }))
  deleteMock.mockReset().mockResolvedValue(undefined)
  toastMock.mockReset()
  feedbackRef.value = ref<ReportFeedbackItem[]>([
    item(),
    item({ id: 'fb-2', comment: 'Second item.', quotedText: 'Net revenue' }),
  ])
})

describe('ReportFeedbackPanel', () => {
  it('is a region named by its heading', () => {
    const panel = mountPanel().get('[data-testid="report-feedback"]')
    const heading = panel.get('h2')

    expect(panel.element.tagName).toBe('SECTION')
    expect(heading.text()).toBe('page.workflowDetails.feedback.title')
    expect(panel.attributes('aria-labelledby')).toBe(heading.attributes('id'))
  })

  it('takes keyboard focus when it is asked to', () => {
    const wrapper = mountPanel('', { attachTo: document.body })

    ;(wrapper.vm as unknown as { focus: () => void }).focus()

    expect(wrapper.get('[data-testid="report-feedback"]').element.contains(document.activeElement)).toBe(true)
    wrapper.unmount()
  })

  it('dates each item the way the report header dates the report', () => {
    const byline = mountPanel().get('[data-testid="feedback-item-fb-1"]').text()

    expect(byline).toContain(formatDateTime('2026-09-09T10:00:00Z'))
    expect(byline).not.toMatch(/AM|PM/)
  })

  it('lists every item on the report with author, quote and status', () => {
    const wrapper = mountPanel()

    expect(wrapper.find('[data-testid="feedback-item-fb-1"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="feedback-item-fb-2"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Grace Hopper')
    expect(wrapper.text()).toContain('Net revenue')
    expect(wrapper.text()).toContain('page.workflowDetails.feedback.statuses.Open')
  })

  it('submits category, comment and the quoted selection', async () => {
    const wrapper = mountPanel('Revenue 120')

    expect(wrapper.find('[data-testid="feedback-quoted"]').text()).toBe('Revenue 120')
    const submit = wrapper.find('[data-testid="feedback-submit"]')
    expect(submit.attributes('disabled')).toBeDefined()

    await wrapper.find('[data-testid="feedback-category"]').setValue(ReportFeedbackCategory.WrongFigure)
    await wrapper.find('textarea').setValue('  Revenue should be 125.  ')
    expect(submit.attributes('disabled')).toBeUndefined()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(createMock).toHaveBeenCalledWith({
      id: 'exec-1',
      params: {
        quotedText: 'Revenue 120',
        category: ReportFeedbackCategory.WrongFigure,
        comment: 'Revenue should be 125.',
      },
    })
    expect((wrapper.find('textarea').element as HTMLTextAreaElement).value).toBe('')
  })

  it('sends no quoted text when nothing was selected', async () => {
    const wrapper = mountPanel()

    expect(wrapper.find('[data-testid="feedback-quoted"]').exists()).toBe(false)
    await wrapper.find('[data-testid="feedback-category"]').setValue(ReportFeedbackCategory.MissingInformation)
    await wrapper.find('textarea').setValue('No liquidity section.')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(createMock).toHaveBeenCalledWith(
      expect.objectContaining({ params: expect.objectContaining({ quotedText: null }) }),
    )
  })

  it('hides the status control from a plain reader', () => {
    setAuthToken({ role: 'User' })
    const wrapper = mountPanel()

    expect(wrapper.find('[data-testid="feedback-status-fb-1"]').exists()).toBe(false)
  })

  it('lets an editor send feedback as themselves and change the status', async () => {

    setAuthToken({ role: 'WorkflowEditor' })
    const wrapper = mountPanel()

    await wrapper.find('[data-testid="feedback-category"]').setValue(ReportFeedbackCategory.NotRelevant)
    await wrapper.find('textarea').setValue('Drop this paragraph.')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(createMock).toHaveBeenCalledWith(
      expect.objectContaining({
        params: { quotedText: null, category: ReportFeedbackCategory.NotRelevant, comment: 'Drop this paragraph.' },
      }),
    )

    await wrapper.find('[data-testid="feedback-status-fb-1"]').setValue(ReportFeedbackStatus.Addressed)
    await flushPromises()

    expect(updateMock).toHaveBeenCalledWith({
      id: 'exec-1',
      feedbackId: 'fb-1',
      params: { status: ReportFeedbackStatus.Addressed, dismissReason: null },
    })
  })

  it('asks for a reason before dismissing', async () => {
    setAuthToken({ role: 'SuperAdmin' })
    const wrapper = mountPanel()

    await wrapper.find('[data-testid="feedback-status-fb-1"]').setValue(ReportFeedbackStatus.Dismissed)
    expect(updateMock).not.toHaveBeenCalled()

    const confirm = wrapper.find('[data-testid="feedback-dismiss-confirm"]')
    expect(confirm.attributes('disabled')).toBeDefined()

    await wrapper.find('[data-testid="feedback-dismiss-reason"]').setValue('Figure matches the filing.')
    await wrapper.find('[data-testid="feedback-dismiss-confirm"]').trigger('click')
    await flushPromises()

    expect(updateMock).toHaveBeenCalledWith({
      id: 'exec-1',
      feedbackId: 'fb-1',
      params: { status: ReportFeedbackStatus.Dismissed, dismissReason: 'Figure matches the filing.' },
    })
  })

  describe('delete', () => {
    let attached: VueWrapper | null = null

    const mountAttached = () => {
      attached = mount(ReportFeedbackPanel, {
        attachTo: document.body,
        props: { executionId: 'exec-1', quotedText: '', open: true },
      })
      return attached
    }

    const settle = async () => {
      await flushPromises()
      await new Promise(resolve => setTimeout(resolve, 10))
      await flushPromises()
    }

    const dialogButton = (label: string) =>
      Array.from(document.querySelectorAll<HTMLButtonElement>('[role="alertdialog"] button')).find(
        button => button.textContent?.trim() === label,
      )

    afterEach(() => {
      attached?.unmount()
      attached = null
      document.body.innerHTML = ''
    })

    it('offers Delete on every item to a plain reader, and asks before deleting', async () => {
      setAuthToken({ role: 'User' })
      const wrapper = mountAttached()

      expect(wrapper.find('[data-testid="feedback-delete-fb-1"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="feedback-delete-fb-2"]').exists()).toBe(true)

      await wrapper.find('[data-testid="feedback-delete-fb-2"]').trigger('click')
      await settle()

      expect(document.querySelector('[role="alertdialog"]')?.textContent).toContain(
        'page.workflowDetails.feedback.deleteTitle',
      )
      expect(deleteMock).not.toHaveBeenCalled()

      dialogButton('page.workflowDetails.feedback.deleteConfirm')!.click()
      await settle()

      expect(deleteMock).toHaveBeenCalledWith({ id: 'exec-1', feedbackId: 'fb-2' })
      expect(toastMock).toHaveBeenCalledWith(
        expect.objectContaining({ description: 'page.workflowDetails.feedback.deleted' }),
      )
      expect(document.querySelector('[role="alertdialog"]')).toBeNull()
    })

    it('deletes nothing when the reader cancels', async () => {
      const wrapper = mountAttached()

      await wrapper.find('[data-testid="feedback-delete-fb-1"]').trigger('click')
      await settle()
      dialogButton('page.workflowDetails.feedback.deleteCancel')!.click()
      await settle()

      expect(deleteMock).not.toHaveBeenCalled()
      expect(document.querySelector('[role="alertdialog"]')).toBeNull()
    })

    it('says so when the delete fails', async () => {
      deleteMock.mockRejectedValue(new Error('boom'))
      const wrapper = mountAttached()

      await wrapper.find('[data-testid="feedback-delete-fb-1"]').trigger('click')
      await settle()
      dialogButton('page.workflowDetails.feedback.deleteConfirm')!.click()
      await settle()

      expect(toastMock).toHaveBeenCalledWith(expect.objectContaining({ variant: 'destructive' }))
    })
  })
})
