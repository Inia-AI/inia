import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'

import { EN } from '@/assets/locales/en'
import useAgentActivityStore from '@/store/agentActivity'
import { WorkflowState } from '@/enums'
import AgentActivityPanel from './AgentActivityPanel.vue'

vi.mock('@/api', () => ({
  default: { AgentActivity: { service: { getBlockActivity: vi.fn(() => new Promise(() => {})) } } },
}))

const report = {
  id: 'exec-1',
  title: 'Part 1 - Corporates',
  workflowState: WorkflowState.Running,
  agentActivity: [
    {
      blockId: 'block-1',
      blockName: 'Income Statement (AGT)',
      round: 1,
      settled: false,
      activity: {
        schemaVersion: 1,
        outcome: 'Running',
        phases: [{ label: 'Getting ready', gapLabel: 'Getting ready…' }],
        beats: [{ atSeconds: 0, phaseIndex: 0, kind: 'Normal', label: 'Reading the statements' }],
        receipt: { documentNames: [], skillNames: [], pagesViewed: 0, codeRuns: 0, checksReported: 0 },
      },
    },
  ],
}

const mountPanel = () =>
  mount(AgentActivityPanel, {
    attachTo: document.body,
    global: {
      plugins: [createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false })],
      stubs: { teleport: true, IniaIcon: true },
    },
  })

const panel = () => document.querySelector('.agent-panel')

describe('AgentActivityPanel keyboard access', () => {
  let opener: HTMLButtonElement

  beforeEach(() => {
    setActivePinia(createPinia())
    opener = document.createElement('button')
    document.body.append(opener)
  })

  afterEach(() => {
    opener.remove()
  })

  const open = async () => {
    const store = useAgentActivityStore()
    store.ingestReport(report)
    const wrapper = mountPanel()
    opener.focus()
    store.openPanel(store.railEntries[0]!.key)
    await flushPromises()
    return { store, wrapper }
  }

  it('moves focus into the panel when it opens', async () => {
    const { wrapper } = await open()

    expect(panel()?.contains(document.activeElement)).toBe(true)
    wrapper.unmount()
  })

  it('keeps focus inside the panel while it is open', async () => {
    const { wrapper } = await open()

    opener.focus()
    await nextTick()

    expect(panel()?.contains(document.activeElement)).toBe(true)
    wrapper.unmount()
  })

  it('returns focus to what opened it when it closes', async () => {
    const { store, wrapper } = await open()

    store.closePanel()
    await flushPromises()
    await new Promise(resolve => setTimeout(resolve))

    expect(document.activeElement).toBe(opener)
    wrapper.unmount()
  })
})
