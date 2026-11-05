import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setAuthToken, clearAuthToken } from '@/test/helpers/jwt'

const { downloadMock } = vi.hoisted(() => ({
  downloadMock: vi.fn(),
}))

vi.mock('@/composables/useDownloadExecutedWorkflow', async () => {
  const { ref } = await vi.importActual<typeof import('vue')>('vue')
  return {
    useDownloadExecutedWorkflow: () => ({
      download: downloadMock,
      isPending: ref(false),
    }),
  }
})

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key),
  }),
}))

// Import after the mocks.
import DownloadExecutedWorkflowButton from './DownloadExecutedWorkflowButton.vue'

beforeEach(() => {
  downloadMock.mockReset()
  downloadMock.mockResolvedValue(undefined)
})

afterEach(() => {
  clearAuthToken()
})

describe('DownloadExecutedWorkflowButton', () => {
  it('renders nothing when the user is not an Operator', () => {
    setAuthToken({ role: 'Admin' })
    const wrapper = mount(DownloadExecutedWorkflowButton, {
      props: { executionId: 'x' },
    })
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('renders for an Operator who is not a SuperAdmin', () => {
    setAuthToken({ role: 'Operator' })
    const wrapper = mount(DownloadExecutedWorkflowButton, {
      props: { executionId: 'x' },
    })
    expect(wrapper.find('button').exists()).toBe(true)
  })

  it('renders nothing when no identifier is supplied', () => {
    setAuthToken({ role: 'SuperAdmin' })
    const wrapper = mount(DownloadExecutedWorkflowButton, { props: {} })
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('renders nothing when every id is null or empty', () => {
    setAuthToken({ role: 'SuperAdmin' })
    const wrapper = mount(DownloadExecutedWorkflowButton, {
      props: { executionId: null, workflowId: '', jobId: undefined },
    })
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('renders an icon button without a text label for variant=icon', () => {
    setAuthToken({ role: 'SuperAdmin' })
    const wrapper = mount(DownloadExecutedWorkflowButton, {
      props: { executionId: 'x', variant: 'icon' },
    })
    const btn = wrapper.get('button')
    expect(btn.exists()).toBe(true)
    // No human-visible label — icon only. Button still exposes a title for a11y.
    expect(btn.text()).toBe('')
    expect(btn.attributes('title')).toBe(
      'shared.workflowDownload.downloadTitle',
    )
  })

  it('renders the default variant with icon + label', () => {
    setAuthToken({ role: 'SuperAdmin' })
    const wrapper = mount(DownloadExecutedWorkflowButton, {
      props: { executionId: 'x', variant: 'default' },
    })
    expect(wrapper.text()).toContain('shared.workflowDownload.downloadLabel')
  })

  it('calls the composable with { executionId } when executionId is supplied', async () => {
    setAuthToken({ role: 'SuperAdmin' })
    const wrapper = mount(DownloadExecutedWorkflowButton, {
      props: { executionId: 'exec-1' },
    })
    await wrapper.get('button').trigger('click')
    expect(downloadMock).toHaveBeenCalledWith({ executionId: 'exec-1' })
  })

  it('calls the composable with { workflowId } when only workflowId is supplied', async () => {
    setAuthToken({ role: 'SuperAdmin' })
    const wrapper = mount(DownloadExecutedWorkflowButton, {
      props: { workflowId: 'wf-1' },
    })
    await wrapper.get('button').trigger('click')
    expect(downloadMock).toHaveBeenCalledWith({ workflowId: 'wf-1' })
  })

  it('calls the composable with { jobId } when only jobId is supplied', async () => {
    setAuthToken({ role: 'SuperAdmin' })
    const wrapper = mount(DownloadExecutedWorkflowButton, {
      props: { jobId: 'job-1' },
    })
    await wrapper.get('button').trigger('click')
    expect(downloadMock).toHaveBeenCalledWith({ jobId: 'job-1' })
  })

  it('stops click propagation so row-level click handlers do not fire', async () => {
    setAuthToken({ role: 'SuperAdmin' })
    const rowClick = vi.fn()
    const wrapper = mount(
      {
        components: { DownloadExecutedWorkflowButton },
        template: `
          <div @click="rowClick">
            <DownloadExecutedWorkflowButton :execution-id="'x'" />
          </div>
        `,
        setup() {
          return { rowClick }
        },
      },
    )
    await wrapper.get('button').trigger('click')
    expect(downloadMock).toHaveBeenCalled()
    expect(rowClick).not.toHaveBeenCalled()
  })

  it('names the row whose workflow it downloads when given one', () => {
    setAuthToken({ role: 'Operator' })
    const button = mount(DownloadExecutedWorkflowButton, {
      props: { jobId: 'job-1', subject: 'Alpha Fund, Aug 2026' },
    }).get('button')

    expect(button.attributes('aria-label')).toBe('shared.workflowDownload.downloadFor:{"subject":"Alpha Fund, Aug 2026"}')
  })
})
