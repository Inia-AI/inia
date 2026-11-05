import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { axiosError } from '@/test/helpers/axiosError'
import { describeReset } from '@/api/base/dailyAllowance'

const refineContent = vi.hoisted(() => vi.fn())
const queryProvenance = vi.hoisted(() => vi.fn())
const activeTenants = vi.hoisted(() => ({ list: [] as { id: string; name: string; capabilities: string[] }[] }))

vi.mock('@/api', async () => {
  const { ref } = await vi.importActual<typeof import('vue')>('vue')
  const mutation = () => ({ mutateAsync: vi.fn(), isPending: ref(false) })
  return {
    default: {
      GraphRag: { useQueryProvenance: () => ({ mutateAsync: queryProvenance, isPending: ref(false) }) },
      ContentRefinement: { useRefineContent: () => ({ mutateAsync: refineContent, isPending: ref(false) }) },
      RefinementPresets: {
        usePresets: () => ({
          data: ref([{ id: 'preset-1', name: 'Executive Summary', instruction: 'Lead with conclusions.' }]),
        }),
        useCreatePreset: mutation,
        useUpdatePreset: mutation,
        useDeletePreset: mutation,
      },
      Users: { useGetTenants: () => ({ data: ref(activeTenants.list), isPending: ref(false) }) },
    },
  }
})

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key),
  }),
}))

import AiPanel from './AiPanel.vue'
import { CAPABILITIES } from '@/constants/capabilities'
import { makeCapabilitiesVary, resetCapabilities, without } from '@/test/helpers/capabilities'

const mountRefineTab = async (content = '<p>text</p>') => {
  const wrapper = mount(AiPanel, {
    props: { isOpen: true, initialTab: 'refine', runId: 'run-1', targetTag: 'tag', content },
    global: { stubs: { MarkdownText: true, ChatInput: true } },
    attachTo: document.body,
  })
  await flushPromises()
  return wrapper
}

describe('AiPanel Refine tab presets row', () => {
  it('names the preset button New preset rather than by its + glyph', async () => {
    const wrapper = await mountRefineTab()

    expect(wrapper.get('button.ai-toolbar-btn').attributes('aria-label')).toBe('shared.aiPanel.presets.newPreset')
    wrapper.unmount()
  })

  it('makes the ? help a focusable button named About presets, described by the help text', async () => {
    const wrapper = await mountRefineTab()

    const help = wrapper.get('.ai-toolbar-help')
    expect(help.element.tagName).toBe('BUTTON')
    expect(help.attributes('type')).toBe('button')
    expect(help.attributes('aria-label')).toBe('shared.aiPanel.presets.aboutPresets')
    const tooltipId = help.attributes('aria-describedby')
    expect(tooltipId).toBeTruthy()
    const tooltip = wrapper.get(`#${tooltipId}`)
    expect(tooltip.attributes('role')).toBe('tooltip')
    expect(tooltip.text()).toBe('shared.aiPanel.presets.helpText')

    ;(help.element as HTMLElement).focus()
    expect(document.activeElement).toBe(help.element)
    wrapper.unmount()
  })

  it('names the edit and delete preset buttons after the preset they act on', async () => {
    const wrapper = await mountRefineTab()

    const [edit, remove] = wrapper.findAll('button.ai-preset-action')
    expect(edit!.attributes('aria-label')).toBe('shared.aiPanel.presets.editPreset:{"name":"Executive Summary"}')
    expect(remove!.attributes('aria-label')).toBe('shared.aiPanel.presets.deletePreset:{"name":"Executive Summary"}')
    wrapper.unmount()
  })

  it('names the fields of the new preset dialog', async () => {
    const wrapper = await mountRefineTab()

    await wrapper.get('button.ai-toolbar-btn').trigger('click')
    await flushPromises()

    const fields = [...document.querySelectorAll('.ai-save-form input, .ai-save-form textarea')]
    expect(fields.map(field => field.getAttribute('aria-label'))).toEqual([
      'shared.aiPanel.presets.nameLabel',
      'shared.aiPanel.presets.instructionLabel',
    ])
    wrapper.unmount()
  })
})

describe('AiPanel Refine tab refined version', () => {
  it('previews the refined text without scripts or event handlers, and applies it as the model returned it', async () => {
    const refined = '<p><strong>Founded</strong> in {{#FACT_1}}.</p><text>Note</text><img src=x onerror=alert(1)><script>alert(1)</script>'
    refineContent.mockResolvedValue({ refinedContent: refined, explanation: 'Tightened.', isOutOfScope: false })
    const wrapper = await mountRefineTab('<p>Founded in <fact id="c0">2011</fact>.</p>')

    await wrapper.get('button.ai-preset-btn').trigger('click')
    await flushPromises()

    const preview = wrapper.get('.chat-preview-content')
    expect(preview.find('script').exists()).toBe(false)
    expect(preview.find('[onerror]').exists()).toBe(false)
    expect(preview.get('strong').text()).toBe('Founded')
    expect(preview.get('fact#c0').text()).toBe('2011')
    expect(preview.text()).toContain('<text>Note</text>')

    await wrapper.get('button.chat-apply-btn').trigger('click')

    expect(wrapper.emitted('accept')![0]![0]).toBe(refined.replace('{{#FACT_1}}', '<fact id="c0">2011</fact>'))
    wrapper.unmount()
  })
})

describe('AiPanel Refine tab refusal', () => {
  it('shows the server validation message when the server refuses the paragraph', async () => {
    const message = 'The paragraph is 62,000 characters, more than the 50,000 that refine takes. Shorten the paragraph, then refine it again.'
    refineContent.mockRejectedValue({
      response: {
        status: 400,
        data: { title: 'One or more validation errors occurred.', errors: { Content: [message] } },
      },
      message: 'Request failed with status code 400',
    })
    const wrapper = await mountRefineTab()

    await wrapper.get('button.ai-preset-btn').trigger('click')
    await flushPromises()

    expect(wrapper.get('.ai-error').text()).toBe(message)
    wrapper.unmount()
  })
})

describe('AiPanel daily allowance', () => {
  const RESETS_AT = '2026-11-04T00:00:00Z'
  const NOW = new Date('2026-11-03T15:00:00Z')

  const exhausted = (kind: string, quota: number) =>
    axiosError(
      429,
      { error: `Server text for ${kind}.`, code: 'DailyAllowance.Exhausted', resetsAt: RESETS_AT },
      undefined,
      { 'ratelimit-policy': `"${kind}";q=${quota};w=86400` },
    )

  const expectedMessage = (kind: string, count: number) => {
    const { time, isToday } = describeReset(new Date(RESETS_AT), { now: NOW })
    const day = isToday ? 'shared.dailyAllowance.today' : 'shared.dailyAllowance.tomorrow'
    return `shared.dailyAllowance.${kind}:${JSON.stringify({ count, time, day })}`
  }

  afterEach(() => {
    vi.useRealTimers()
  })

  it('tells the reader when explore opens again once the daily questions are used', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(NOW)
    queryProvenance.mockRejectedValue(exhausted('explore', 7))
    const wrapper = mount(AiPanel, {
      props: { isOpen: true, initialTab: 'explore', runId: 'run-1', targetTag: 'tag', content: '<p>text</p>' },
      global: { stubs: { MarkdownText: true, ChatInput: true } },
      attachTo: document.body,
    })
    await flushPromises()

    const input = wrapper.findComponent({ name: 'ChatInput' })
    input.vm.$emit('update:modelValue', 'Where does this figure come from?')
    input.vm.$emit('submit')
    await flushPromises()

    expect(wrapper.get('.ai-error').text()).toBe(expectedMessage('explore', 7))
    expect(input.attributes('disabled')).toBe('false')
    wrapper.unmount()
  })

  it('tells the reader when refine opens again once the daily requests are used', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(NOW)
    refineContent.mockRejectedValue(exhausted('refine', 5))
    const wrapper = await mountRefineTab()

    await wrapper.get('button.ai-preset-btn').trigger('click')
    await flushPromises()

    expect(wrapper.get('.ai-error').text()).toBe(expectedMessage('refine', 5))
    wrapper.unmount()
  })
})

describe('AiPanel and the organization’s capabilities', () => {
  afterEach(resetCapabilities)

  const withCapabilities = (capabilities: string[]) => {
    activeTenants.list = makeCapabilitiesVary(capabilities)
  }

  it('offers only Refine, and opens on it, to an organization without explore', async () => {
    withCapabilities(without(CAPABILITIES.EXPLORE))

    const wrapper = mount(AiPanel, {
      props: { isOpen: true, initialTab: 'explore', runId: 'run-1', targetTag: 'tag', content: '<p>text</p>' },
      global: { stubs: { MarkdownText: true, ChatInput: true } },
      attachTo: document.body,
    })
    await flushPromises()

    expect(wrapper.findAll('[role="tab"]').map(tab => tab.text())).toEqual(['shared.aiPanel.tabs.refine'])
    expect(wrapper.text()).toContain('shared.aiPanel.refine.description')
    wrapper.unmount()
  })

  it('offers only Explore, with no way to send its answer to Refine, to an organization without refine', async () => {
    withCapabilities(without(CAPABILITIES.REFINE))

    const wrapper = mount(AiPanel, {
      props: { isOpen: true, initialTab: 'refine', runId: 'run-1', targetTag: 'tag', content: '<p>text</p>' },
      global: { stubs: { MarkdownText: true, ChatInput: true } },
      attachTo: document.body,
    })
    await flushPromises()

    expect(wrapper.findAll('[role="tab"]').map(tab => tab.text())).toEqual(['shared.aiPanel.tabs.explore'])
    wrapper.unmount()
  })
})
