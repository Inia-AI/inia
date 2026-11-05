import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'

const { resolveMock, openMock } = vi.hoisted(() => ({
  resolveMock: vi.fn((to: unknown) => ({
    href: `/documents?jobId=${(to as { query: { jobId: string } }).query.jobId}`,
  })),
  openMock: vi.fn(),
}))

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key),
  }),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ resolve: resolveMock }),
}))

import OpenDocumentsButton from './OpenDocumentsButton.vue'

describe('OpenDocumentsButton', () => {
  beforeEach(() => {
    resolveMock.mockClear()
    openMock.mockReset()
    vi.stubGlobal('open', openMock)
  })

  it('renders nothing when jobId is null', () => {
    const wrapper = mount(OpenDocumentsButton, { props: { jobId: null } })
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('renders nothing when jobId is an empty string', () => {
    const wrapper = mount(OpenDocumentsButton, { props: { jobId: '' } })
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('renders nothing when jobId is undefined', () => {
    const wrapper = mount(OpenDocumentsButton)
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('renders a button when jobId is a non-empty string', () => {
    const wrapper = mount(OpenDocumentsButton, { props: { jobId: 'abc' } })
    expect(wrapper.find('button').exists()).toBe(true)
  })

  it('icon variant (default) does not render the label text', () => {
    const wrapper = mount(OpenDocumentsButton, { props: { jobId: 'abc' } })
    expect(wrapper.text()).not.toContain('shared.openDocuments.label')
  })

  it('default variant renders the label text', () => {
    const wrapper = mount(OpenDocumentsButton, {
      props: { jobId: 'abc', variant: 'default' },
    })
    expect(wrapper.text()).toContain('shared.openDocuments.label')
  })

  it('click opens the Documents route for the jobId in a new tab', async () => {
    const wrapper = mount(OpenDocumentsButton, { props: { jobId: 'job-123' } })
    await wrapper.find('button').trigger('click')
    expect(resolveMock).toHaveBeenCalledWith({
      name: 'Documents',
      query: { jobId: 'job-123' },
    })
    expect(openMock).toHaveBeenCalledWith(
      '/documents?jobId=job-123',
      '_blank',
      'noopener,noreferrer',
    )
  })

  it('stops click propagation so parent row-click handlers do not fire', async () => {
    const parentHandler = vi.fn()
    const Parent = {
      components: { OpenDocumentsButton },
      template: `<div @click="onParentClick"><OpenDocumentsButton job-id="abc" /></div>`,
      setup() {
        return { onParentClick: parentHandler }
      },
    }
    const wrapper = mount(Parent)
    await wrapper.find('button').trigger('click')
    expect(parentHandler).not.toHaveBeenCalled()
  })

  it('names the row it opens documents for when given one', () => {
    const button = mount(OpenDocumentsButton, { props: { jobId: 'job-1', subject: 'Alpha Fund, Aug 2026' } }).get('button')

    expect(button.attributes('aria-label')).toBe('shared.openDocuments.openFor:{"subject":"Alpha Fund, Aug 2026"}')
    expect(button.attributes('title')).toBe(button.attributes('aria-label'))
  })
})
