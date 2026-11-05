import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (key: string) => key }),
}))

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import WorkflowElementWrapper from './WorkflowElementWrapper.vue'
import { CAPABILITIES } from '@/constants/capabilities'
import { answerCapabilities, resetCapabilities, without } from '@/test/helpers/capabilities'

const mountWrapper = () =>
  mount(WorkflowElementWrapper, {
    props: { actions: ['trace', 'edit', 'copy', 'remove'], showActions: true },
    global: { stubs: { IniaIcon: true } },
    attachTo: document.body,
  })

describe('WorkflowElementWrapper actions', () => {
  it('offers Explore Analysis as a named native button that takes keyboard focus', async () => {
    const wrapper = mountWrapper()

    const explore = wrapper.get('.ai-trace-button')
    expect(explore.element.tagName).toBe('BUTTON')
    expect(explore.attributes('type')).toBe('button')
    expect(explore.attributes('aria-label')).toBe('shared.tracer.title')

    ;(explore.element as HTMLButtonElement).focus()
    expect(document.activeElement).toBe(explore.element)

    await explore.trigger('click')
    expect(wrapper.emitted('trace')).toHaveLength(1)
    wrapper.unmount()
  })

  it('offers every paragraph action as a named native button', async () => {
    const wrapper = mountWrapper()

    const actions = wrapper.findAll('button.element-action')
    expect(actions.map(action => action.attributes('aria-label'))).toEqual([
      'shared.components.workflowElement.actions.edit',
      'shared.components.workflowElement.actions.copy',
      'shared.components.workflowElement.actions.remove',
    ])
    for (const action of actions) {
      expect(action.attributes('type')).toBe('button')
      await action.trigger('click')
    }

    expect(wrapper.emitted('edit')).toHaveLength(1)
    expect(wrapper.emitted('copy')).toHaveLength(1)
    expect(wrapper.emitted('remove')).toHaveLength(1)
    wrapper.unmount()
  })

  it('names the heading edit apart from the paragraph edit, and both emit edit', async () => {
    const wrapper = mount(WorkflowElementWrapper, {
      props: { actions: ['editHeading'], showActions: true },
      global: { stubs: { IniaIcon: true } },
    })

    const edit = wrapper.get('button.element-action')
    expect(edit.attributes('aria-label')).toBe('shared.components.workflowElement.actions.editHeading')

    await edit.trigger('click')
    expect(wrapper.emitted('edit')).toHaveLength(1)
  })
})

describe('WorkflowElementWrapper and the organization’s capabilities', () => {
  afterEach(resetCapabilities)

  const mountFor = async (capabilities: string[]) => {
    answerCapabilities(capabilities)
    const wrapper = mount(WorkflowElementWrapper, {
      props: { actions: ['trace', 'edit'], showActions: true },
      global: { stubs: { IniaIcon: true }, plugins: [[VueQueryPlugin, { queryClient: new QueryClient() }]] },
    })
    await flushPromises()
    return wrapper
  }

  it('offers Explore Analysis while the organization can explore or refine', async () => {
    const wrapper = await mountFor(without(CAPABILITIES.EXPLORE))

    expect(wrapper.find('.ai-trace-button').exists()).toBe(true)
  })

  it('drops Explore Analysis for an organization that can neither explore nor refine, and keeps the other actions', async () => {
    const wrapper = await mountFor(without(CAPABILITIES.EXPLORE, CAPABILITIES.REFINE))

    expect(wrapper.find('.ai-trace-button').exists()).toBe(false)
    expect(wrapper.findAll('button.element-action').map(action => action.attributes('aria-label'))).toEqual([
      'shared.components.workflowElement.actions.edit',
    ])
  })
})
