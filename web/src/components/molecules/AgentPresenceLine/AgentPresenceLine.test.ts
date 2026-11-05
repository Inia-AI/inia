import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'

import { EN } from '@/assets/locales/en'
import type { AgentRailEntry } from '@/store/agentActivity'
import AgentPresenceLine from './AgentPresenceLine.vue'

const NOW = 1_755_770_000_000

const makeEntry = (
  overrides: Partial<AgentRailEntry> = {},
): AgentRailEntry => ({
  key: 'exec-1:block-1',
  executionId: 'exec-1',
  executionTitle: 'Q2 Credit Review',
  counterpartyName: 'Acme Capital',
  blockId: 'block-1',
  blockName: 'Balance Sheet',
  round: 1,
  role: 'worker',
  settled: false,
  digest: null,
  finalDigest: null,
  displayDigest: null,
  lastSeenAt: NOW,
  lastBeatAt: NOW,
  ...overrides,
})

const mountLine = (entry: AgentRailEntry, fileName?: string) =>
  mount(AgentPresenceLine, {
    props: { entry, now: NOW, fileName },
    global: {
      plugins: [
        createI18n({
          legacy: false,
          locale: 'en',
          messages: { en: EN },
          missingWarn: false,
          fallbackWarn: false,
        }),
      ],
      stubs: { IniaIcon: true, AgentPresenceDot: true },
    },
  })

describe('AgentPresenceLine.vue', () => {
  it('renders the specialism as an agent role, not an analyst', () => {
    const wrapper = mountLine(makeEntry())
    expect(wrapper.find('.presence-role').text()).toBe('Balance Sheet agent')
    expect(wrapper.text()).not.toMatch(/analyst/i)
  })

  it('names a verifier leg as the verifier, not as one more agent', () => {
    const wrapper = mountLine(makeEntry({ role: 'verifier', blockName: 'Group review' }))
    expect(wrapper.find('.presence-role').text()).toBe('Group review verifier')
  })

  it('does not repeat a role noun the block name already ends with', () => {
    const verifier = mountLine(
      makeEntry({ role: 'verifier', blockName: 'Statement verifier' }),
    )
    expect(verifier.find('.presence-role').text()).toBe('Statement verifier')
    expect(verifier.attributes('aria-label')).toBe(
      'Open the record of the Statement verifier',
    )

    const worker = mountLine(makeEntry({ blockName: 'Income agent' }))
    expect(worker.find('.presence-role').text()).toBe('Income agent')
  })

  it('reads the other role’s noun too, and only as a whole word', () => {
    // A worker block a prompt engineer named for reviewing, and a verifier named for extracting:
    // each ends in the noun the other role would append.
    const workerNamedVerifier = mountLine(makeEntry({ blockName: 'Statement verifier' }))
    expect(workerNamedVerifier.find('.presence-role').text()).toBe('Statement verifier')

    const verifierNamedAgent = mountLine(
      makeEntry({ role: 'verifier', blockName: 'Income agent' }),
    )
    expect(verifierNamedAgent.find('.presence-role').text()).toBe('Income agent')

    // "Reagent" merely ends in the letters, so it keeps its role.
    expect(mountLine(makeEntry({ blockName: 'Reagent' })).find('.presence-role').text()).toBe(
      'Reagent agent',
    )
  })

  it('renders an unnamed block without a stray role', () => {
    const wrapper = mountLine(makeEntry({ blockName: '' }))
    expect(wrapper.find('.presence-role').text()).toBe('')
    expect(wrapper.attributes('aria-label')).toBe('Open the record of the')
  })

  it('says which review round a redone member is on, and says nothing on the first', () => {
    const redone = mountLine(makeEntry({ round: 2 }))
    expect(redone.find('.presence-tail').text()).toContain('review round 2')

    const firstRound = mountLine(makeEntry())
    expect(firstRound.text()).not.toMatch(/review round/i)
  })

  it('labels the open-feed affordance with agent vocabulary', () => {
    const withFile = mountLine(makeEntry(), 'Acme Capital')
    expect(withFile.attributes('aria-label')).toBe(
      'Open the record of the Balance Sheet agent on Acme Capital',
    )

    const withoutFile = mountLine(makeEntry())
    expect(withoutFile.attributes('aria-label')).toBe(
      'Open the record of the Balance Sheet agent',
    )
  })
})
