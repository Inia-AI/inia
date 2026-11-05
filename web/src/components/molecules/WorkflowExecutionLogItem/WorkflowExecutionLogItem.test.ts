import { afterEach, describe, it, expect } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'

import WorkflowExecutionLogItem from './WorkflowExecutionLogItem.vue'
import type {
  WorkflowExecutionLogDetail,
  WorkflowExecutionLogSummary,
} from '@/api/workflowExecutions/schema'

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  messages: { en: EN },
  missingWarn: false,
  fallbackWarn: false,
})

const LOG: WorkflowExecutionLogSummary = {
  id: 'log-1',
  blockName: 'Fixture page',
  status: 'Completed',
  hasPrompt: true,
  hasOutput: true,
}

const DETAIL: WorkflowExecutionLogDetail = {
  id: 'log-1',
  prompt: 'Summarise {counterparty} for the committee.',
  output: 'Acme Corp is a manufacturer.',
  promptPlaceholderValues: { counterparty: { value: 'Acme Co', translated: false, truncated: true } },
}

let wrapper: VueWrapper | null = null

const mountItem = (props: Record<string, unknown> = {}) => {
  wrapper = mount(WorkflowExecutionLogItem, {
    attachTo: document.body,
    props: { log: LOG, ...props },
    global: { plugins: [i18n], stubs: { IniaIcon: true } },
  })
  return wrapper
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = null
  document.body.innerHTML = ''
})

const visibleLabel = (element: Element) => {
  const copy = element.cloneNode(true) as Element
  copy.querySelectorAll('[aria-hidden="true"]').forEach(hidden => hidden.remove())
  return copy.textContent!.replace(/\s+/g, ' ').trim()
}

const accessibleName = (element: Element) => element.getAttribute('aria-label') || visibleLabel(element)

const toggleNamed = (name: string) =>
  wrapper!.findAll('button').find(button => accessibleName(button.element) === name)

const forFixtureBlock = (label: string) => `${label} for ${LOG.blockName}`

const toggleFor = (label: string) => toggleNamed(forFixtureBlock(label))

describe('WorkflowExecutionLogItem toggles', () => {
  it.each([
    ['Show Prompt', 'Hide Prompt', 'prompt'],
    ['Show Output', 'Hide Output', 'output'],
  ])('renders %s as a collapsed button that expands and asks for the detail', async (show, hide, contentType) => {
    mountItem()
    const toggle = toggleFor(show)

    expect(toggle).toBeDefined()
    expect(toggle!.attributes('type')).toBe('button')
    expect(toggle!.attributes('aria-expanded')).toBe('false')

    await toggle!.trigger('click')

    expect(toggle!.attributes('aria-expanded')).toBe('true')
    expect(accessibleName(toggle!.element)).toBe(forFixtureBlock(hide))
    expect(wrapper!.emitted('load-detail')).toEqual([['log-1', contentType]])
  })

  it.each([
    ['at rest', {}],
    ['while loading', { isLoadingPrompt: true, isLoadingOutput: true }],
  ])('names each toggle after its block and starts the name with the words on screen %s', (_, props) => {
    mountItem({ log: { ...LOG, blockName: 'QA summary of the figures' }, ...props })
    const buttons = wrapper!.findAll('button')

    expect(buttons.map(button => accessibleName(button.element))).toEqual([
      'Show Prompt for QA summary of the figures',
      'Show Output for QA summary of the figures',
    ])
    for (const button of buttons) {
      expect(accessibleName(button.element).startsWith(visibleLabel(button.element))).toBe(true)
    }
  })

  it('adds the section to each toggle\'s name, so one block run in two sections gets two names', () => {
    mountItem({ log: { ...LOG, blockName: 'Bank statement prep', sectionName: 'Account 2' } })

    expect(wrapper!.findAll('button').map(button => accessibleName(button.element))).toEqual([
      'Show Prompt for Bank statement prep in Account 2',
      'Show Output for Bank statement prep in Account 2',
    ])
  })

  it('falls back to the words on screen for a block without a name', () => {
    mountItem({ log: { ...LOG, blockName: '  ' } })

    expect(wrapper!.findAll('button').map(button => accessibleName(button.element))).toEqual([
      'Show Prompt',
      'Show Output',
    ])
  })

  it.each([
    ['Show Prompt', 'Hide Prompt'],
    ['Show Output', 'Hide Output'],
  ])('keeps the arrow of %s on screen but out of its name', async (show, hide) => {
    mountItem({ logDetail: DETAIL })
    const toggle = toggleFor(show)!

    expect(toggle.text()).toBe(`${show} ▼`)
    expect(toggle.get('[aria-hidden="true"]').text()).toBe('▼')

    await toggle.trigger('click')

    expect(toggle.text()).toBe(`${hide} ▲`)
    expect(toggle.get('[aria-hidden="true"]').text()).toBe('▲')
  })

  it.each([
    ['Show Prompt', 'Summarise'],
    ['Show Output', 'Acme Corp is a manufacturer.'],
  ])('points %s at the region it opens, which holds the detail once open', async (show, detailText) => {
    mountItem({ logDetail: DETAIL })
    const toggle = toggleFor(show)!
    const regionId = toggle.attributes('aria-controls')

    expect(regionId).toBeTruthy()
    expect(document.getElementById(regionId!)).not.toBeNull()
    expect(document.getElementById(regionId!)!.textContent).not.toContain(detailText)

    await toggle.trigger('click')

    expect(document.getElementById(regionId!)!.textContent).toContain(detailText)
  })

  it('gives the prompt and the output toggles different regions', () => {
    mountItem()

    expect(toggleFor('Show Prompt')!.attributes('aria-controls')).not.toBe(
      toggleFor('Show Output')!.attributes('aria-controls'),
    )
  })

  it('shows the hourglass of a loading toggle beside its arrow, both hidden from assistive technology', () => {
    mountItem({ isLoadingPrompt: true })

    expect(toggleFor('Show Prompt')!.findAll('[aria-hidden="true"]').map(glyph => glyph.text())).toEqual([
      '⌛',
      '▼',
    ])
  })
})

describe('WorkflowExecutionLogItem rendered prompt and output', () => {
  const HOSTILE_MARKUP = '<img src=x onerror=alert(1)><script>alert(1)</script>'

  const openRegion = async (show: string, logDetail: WorkflowExecutionLogDetail) => {
    mountItem({ logDetail })
    const toggle = toggleFor(show)!
    await toggle.trigger('click')
    return document.getElementById(toggle.attributes('aria-controls')!)!
  }

  it('drops scripts and event handlers from the output and keeps its formatting', async () => {
    const region = await openRegion('Show Output', {
      ...DETAIL,
      output: `<strong>Revenue</strong> rose.\n<table><tr><td>12%</td></tr></table>${HOSTILE_MARKUP}`,
    })

    expect(region.querySelector('script')).toBeNull()
    expect(region.querySelector('[onerror]')).toBeNull()
    expect(region.querySelector('strong')!.textContent).toBe('Revenue')
    expect(region.querySelector('td')!.textContent).toBe('12%')
    expect(region.textContent).toContain('rose.\n')
  })

  it('shows a tag the model wrote, such as its stop signal, as text', async () => {
    const region = await openRegion('Show Output', { ...DETAIL, output: '<stop>OK</stop>\nThe document loaded.' })

    expect(region.textContent).toContain('<stop>OK</stop>\nThe document loaded.')
  })

  it.each([
    ['with placeholder values', DETAIL.promptPlaceholderValues],
    ['without placeholder values', {}],
  ])('drops scripts and event handlers from the prompt %s', async (_, promptPlaceholderValues) => {
    const region = await openRegion('Show Prompt', {
      ...DETAIL,
      prompt: `Summarise {counterparty}.${HOSTILE_MARKUP}`,
      promptPlaceholderValues,
    })

    expect(region.querySelector('script')).toBeNull()
    expect(region.querySelector('[onerror]')).toBeNull()
    expect(region.textContent).toContain('Summarise')
  })

  it('shows markup in a placeholder value as text', async () => {
    const region = await openRegion('Show Prompt', {
      ...DETAIL,
      promptPlaceholderValues: { counterparty: { value: HOSTILE_MARKUP, translated: false, truncated: false } },
    })

    expect(region.querySelector('script')).toBeNull()
    expect(region.querySelector('[onerror]')).toBeNull()
    expect(region.querySelector('[title="Placeholder: counterparty"]')!.textContent).toContain(HOSTILE_MARKUP)
  })

  it.each([["US$'000"], ['a $& b'], ['cost $$5'], ['x $` y']])(
    'shows a placeholder value holding %s exactly as written',
    async value => {
      const region = await openRegion('Show Prompt', {
        ...DETAIL,
        prompt: 'Revenue in {unit} for FY24.',
        promptPlaceholderValues: { unit: { value, translated: false, truncated: false } },
      })

      expect(region.textContent!.replace(/\s+/g, ' ')).toBe(`Revenue in ${value} for FY24.`)
    },
  )

  it('keeps the placeholder highlight, its tooltips and the line breaks of the prompt', async () => {
    const region = await openRegion('Show Prompt', {
      ...DETAIL,
      prompt: 'You are an analyst.\nSummarise {counterparty} for the committee.',
    })

    const placeholder = region.querySelector('[title="Placeholder: counterparty (truncated)"]')!
    expect(placeholder.tagName).toBe('SPAN')
    expect(placeholder.classList).toContain('cursor-help')
    expect(placeholder.textContent).toContain('Acme Co')
    expect(placeholder.querySelector('[title="Value is truncated"]')!.textContent).toBe('⚠️')
    expect(region.textContent).toContain('You are an analyst.\nSummarise')
  })
})

describe('WorkflowExecutionLogItem prompt with no preview', () => {
  const NO_PREVIEW = 'No prompt preview was recorded for this block.'

  const openPrompt = async (props: Record<string, unknown>) => {
    mountItem(props)
    const toggle = toggleFor('Show Prompt')!
    await toggle.trigger('click')
    return document.getElementById(toggle.attributes('aria-controls')!)!
  }

  it.each([[''], ['  \n ']])('says no prompt preview was recorded for a prompt that came back as %j', async prompt => {
    const region = await openPrompt({ logDetail: { ...DETAIL, prompt } })

    expect(region.textContent!.trim()).toBe(NO_PREVIEW)
    expect(wrapper!.emitted('load-detail')).toBeUndefined()
  })

  it('says it is loading, not that there is no preview, while the prompt loads', async () => {
    const region = await openPrompt({ isLoadingPrompt: true })

    expect(region.textContent!.trim()).toBe(EN.page.workflowExecutionLogs.item.loadingPrompt)
  })

  it('claims nothing about a prompt it has not loaded yet', async () => {
    const region = await openPrompt({})

    expect(region.textContent!.trim()).toBe('')
  })
})

describe('WorkflowExecutionLogItem placeholder values toggle', () => {
  const openPrompt = async (props: Record<string, unknown>) => {
    mountItem({ logDetail: DETAIL, ...props })
    await toggleFor('Show Prompt')!.trigger('click')
  }

  it('offers the full values while the prompt shows truncated ones, with its glyph hidden', async () => {
    await openPrompt({})

    const toggle = toggleFor('Show Full Values')!
    expect(toggle.attributes('type')).toBe('button')
    expect(toggle.get('[aria-hidden="true"]').text()).toBe('🔍')
  })

  it('offers the truncated values back while the prompt shows full ones', async () => {
    await openPrompt({ isShowingFullValues: true, fullPlaceholders: { counterparty: 'Acme Corporation' } })

    const toggle = toggleFor('Show Truncated Values')!
    expect(toggle.get('[aria-hidden="true"]').text()).toBe('📄')
  })

  it('says it is loading while the full values are fetched', async () => {
    await openPrompt({ isLoadingPlaceholders: true })

    const toggle = toggleNamed(EN.common.loading)!
    expect(toggle.attributes('disabled')).toBeDefined()
    expect(toggle.text()).toContain('⌛')
  })
})

describe('WorkflowExecutionLogItem output with nothing recorded', () => {
  const NO_OUTPUT = 'No output was recorded for this block.'

  const openOutput = async (props: Record<string, unknown>) => {
    mountItem(props)
    const toggle = toggleFor('Show Output')!
    await toggle.trigger('click')
    return document.getElementById(toggle.attributes('aria-controls')!)!
  }

  it.each([[''], ['  \n ']])('says no output was recorded for an output that came back as %j', async output => {
    const region = await openOutput({ logDetail: { ...DETAIL, output } })

    expect(region.textContent!.trim()).toBe(NO_OUTPUT)
    expect(wrapper!.emitted('load-detail')).toBeUndefined()
  })

  it('says it is loading, not that there is no output, while the output loads', async () => {
    const region = await openOutput({ isLoadingOutput: true })

    expect(region.textContent!.trim()).toBe(EN.page.workflowExecutionLogs.item.loadingOutput)
  })

  it('claims nothing about an output it has not loaded yet', async () => {
    const region = await openOutput({})

    expect(region.textContent!.trim()).toBe('')
  })
})

describe('WorkflowExecutionLogItem model refusal', () => {
  it('marks a refused block and says why, beside its Failed status', () => {
    const item = mountItem({
      log: { ...LOG, status: 'Failed', refusalCategory: 'reasoning_extraction', error: 'The model refused to answer this block (refusal category: reasoning_extraction), so the block has no output.' },
    })

    expect(item.find('.status-failed').exists()).toBe(true)
    expect(item.get('.log-refusal').text().replace(/\s+/g, ' ')).toBe('Refused by the model (refusal category: reasoning_extraction)')
    expect(item.get('.log-error').text()).toContain('so the block has no output')
  })

  it('shows no refusal mark on a block that failed for another reason', () => {
    const item = mountItem({ log: { ...LOG, status: 'Failed', error: 'Timeout' } })

    expect(item.find('.log-refusal').exists()).toBe(false)
  })
})
