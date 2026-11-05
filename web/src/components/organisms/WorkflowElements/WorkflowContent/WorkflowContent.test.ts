import { afterEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { EN } from '@/assets/locales/en'
import WorkflowContent from './WorkflowContent.vue'
import { OPEN_REPORT_FEEDBACK_KEY } from '@/components/organisms/ReportFeedback/injectionKeys'
import bankStatementCards from '@/components/organisms/TiptapEditor/fixtures/bankStatementCards.html?raw'
import type { Citation } from '@/api/workflows/schema'
import type { Editor } from '@tiptap/vue-3'
import { replaceWords } from '@/test/helpers/tiptapEditor'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false, fallbackWarn: false })

describe('WorkflowContent applied refinement', () => {
  it('shows a refined text saved without edits without its scripts or event handlers, and saves it as returned', async () => {
    const refined = '<p>Refined</p><img src=x onerror=alert(1)><script>alert(1)</script>'
    const wrapper = mount(WorkflowContent, {
      props: { elementId: 'p1', workflowId: 'run-1', content: '<p>Original</p>' },
      global: { plugins: [i18n], stubs: { AiPanel: true, TiptapEditor: true } },
    })

    wrapper.findComponent({ name: 'AiPanel' }).vm.$emit('accept', refined)
    await nextTick()
    await wrapper.findAll('button').find(button => button.text() === 'Save')!.trigger('click')

    const shown = wrapper.get('.workflow-content-text')
    expect(shown.text()).toContain('Refined')
    expect(shown.find('[onerror]').exists()).toBe(false)
    expect(shown.find('script').exists()).toBe(false)
    expect(wrapper.emitted('save')![0]![0]).toEqual({ id: 'p1', content: refined })
  })

  it('copies an applied refinement without putting its event handlers into the page', async () => {
    const wrapper = mount(WorkflowContent, {
      props: { elementId: 'p1', workflowId: 'run-1', content: '<p>Original</p>', actions: ['copy'] },
      global: { plugins: [i18n], stubs: { AiPanel: true, TiptapEditor: true } },
    })
    wrapper.findComponent({ name: 'AiPanel' }).vm.$emit('accept', '<p>Refined</p><img src=x onerror=alert(1)>')
    await nextTick()
    await wrapper.findAll('button').find(button => button.text() === 'Save')!.trigger('click')
    Object.assign(navigator, { clipboard: { writeText: vi.fn().mockResolvedValue(undefined) } })
    const attached: string[] = []
    const appendChild = vi.spyOn(document.body, 'appendChild').mockImplementation(node => {
      attached.push((node as HTMLElement).innerHTML)
      return node
    })
    vi.spyOn(document.body, 'removeChild').mockImplementation(node => node)

    wrapper.findComponent({ name: 'WorkflowElementWrapper' }).vm.$emit('copy')
    await flushPromises()

    expect(attached.join('')).toContain('Refined')
    expect(attached.join('')).not.toContain('onerror')
    appendChild.mockRestore()
  })
})

describe('WorkflowContent editor from the keyboard', () => {
  const editors: { kind: string; actions?: string[]; useEditor: boolean; editLabel: string; fieldName: string }[] = [
    { kind: 'paragraph', actions: ['edit'], useEditor: true, editLabel: 'Edit paragraph', fieldName: 'Paragraph text' },
    { kind: 'heading', actions: ['editHeading'], useEditor: false, editLabel: 'Edit heading', fieldName: 'Heading text' },
    { kind: 'heading beside every other action', useEditor: false, editLabel: 'Edit heading', fieldName: 'Heading text' },
  ]

  const openEditor = async ({ actions, useEditor, editLabel }: (typeof editors)[number]) => {
    const wrapper = mount(WorkflowContent, {
      props: { elementId: 'p1', workflowId: 'run-1', content: '<p>Revenue was 12m</p>', actions, useEditor },
      global: { plugins: [i18n], stubs: { AiPanel: true } },
      attachTo: document.body,
    })
    const edit = wrapper.get(`button[aria-label="${editLabel}"]`).element as HTMLButtonElement
    edit.focus()
    edit.click()
    await flushPromises()
    return wrapper
  }

  const textField = (name: string) => document.querySelector<HTMLElement>(`[aria-label="${name}"]`)

  it.each(editors)('opens the $kind editor as a named text field with focus inside it', async editor => {
    await openEditor(editor)

    await vi.waitFor(() => expect(textField(editor.fieldName)?.contains(document.activeElement)).toBe(true))
  })

  it('offers a heading for editing as its plain text, without the markup it is shown with', async () => {
    const wrapper = mount(WorkflowContent, {
      props: { elementId: 'h1', workflowId: 'run-1', content: 'Revenue and margin', actions: ['editHeading'], useEditor: false },
      global: { plugins: [i18n], stubs: { AiPanel: true } },
      attachTo: document.body,
    })

    ;(wrapper.get('button[aria-label="Edit heading"]').element as HTMLButtonElement).click()
    await flushPromises()
    expect((textField('Heading text') as HTMLInputElement).value).toBe('Revenue and margin')

    wrapper.findAll('button').find(button => button.text() === 'Save')!.element.click()
    expect(wrapper.emitted('save')![0]![0]).toEqual({ id: 'h1', content: 'Revenue and margin' })
  })

  it('offers a cited heading as its words, without the passage its citation carries', async () => {
    const content =
      'Revenue of <fact>12m<fact_source label="Annual report" page-id="p-3"><cited-text>Revenue 12,000,000</cited-text></fact_source></fact>'
    const wrapper = mount(WorkflowContent, {
      props: { elementId: 'h1', workflowId: 'run-1', content, actions: ['editHeading'], useEditor: false },
      global: { plugins: [i18n], stubs: { AiPanel: true } },
      attachTo: document.body,
    })

    ;(wrapper.get('button[aria-label="Edit heading"]').element as HTMLButtonElement).click()
    await flushPromises()

    expect((textField('Heading text') as HTMLInputElement).value).toBe('Revenue of 12m')
    wrapper.unmount()
  })

  it('opens a heading as its named text field with focus inside it when a refinement is accepted', async () => {
    const wrapper = mount(WorkflowContent, {
      props: { elementId: 'h1', workflowId: 'run-1', content: 'Revenue', actions: ['editHeading'], useEditor: false },
      global: { plugins: [i18n], stubs: { AiPanel: true } },
      attachTo: document.body,
    })

    wrapper.findComponent({ name: 'AiPanel' }).vm.$emit('accept', 'Revenue and margin')
    await flushPromises()

    expect(document.activeElement).toBe(textField('Heading text'))
  })

  it.each(editors.flatMap(editor => ['Cancel', 'Save'].map(leaveWith => ({ ...editor, leaveWith }))))(
    'returns focus to $editLabel when $leaveWith closes the $kind editor',
    async editor => {
      const wrapper = await openEditor(editor)
      await vi.waitFor(() => expect(textField(editor.fieldName)).not.toBeNull())

      wrapper.findAll('button').find(button => button.text() === editor.leaveWith)!.element.click()
      await flushPromises()

      expect(document.activeElement).toBe(wrapper.get(`button[aria-label="${editor.editLabel}"]`).element)
    },
  )
})

describe('WorkflowContent citations through an edit', () => {
  it('saves every citation of a section as stored, and shows them as working citations after the save', async () => {
    const citationIds = [...bankStatementCards.matchAll(/<fact id="(c\d+)">/g)].map(match => match[1]!)
    const citations = citationIds.map(id => ({ id, sources: [{ type: 'Document' }] })) as unknown as Citation[]
    const wrapper = mount(WorkflowContent, {
      props: { elementId: 'p1', workflowId: 'run-1', content: bankStatementCards, actions: ['edit'], citations },
      global: { plugins: [i18n], stubs: { AiPanel: true } },
      attachTo: document.body,
    })
    ;(wrapper.get('button[aria-label="Edit paragraph"]').element as HTMLButtonElement).click()
    await flushPromises()
    const editor = (wrapper.findComponent({ name: 'TiptapEditor' }).vm as unknown as { editor: Editor }).editor
    replaceWords(editor, 'Period credits', 'Period inflows')
    await flushPromises()

    wrapper.findAll('button').find(button => button.text() === 'Save')!.element.click()
    await flushPromises()

    const saved = (wrapper.emitted('save')![0]![0] as { content: string }).content
    expect(saved).toBe(bankStatementCards.trim().replace('Period credits', 'Period inflows'))
    const shownCitations = [...wrapper.get('.workflow-content-text').element.querySelectorAll('fact')]
    expect(shownCitations.map(fact => fact.id)).toEqual(citationIds)
    expect(shownCitations.every(fact => fact.getAttribute('role') === 'button')).toBe(true)
    wrapper.unmount()
  })
})

describe('WorkflowContent paragraph facts', () => {
  it('offers the paragraph sources as a control a keyboard user can reach', () => {
    const wrapper = mount(WorkflowContent, {
      props: { elementId: 'p1', content: '<p>Revenue was 12m</p>', sources: ['Annual report', 'Q3 filing'] },
      global: { plugins: [i18n], stubs: { AiPanel: true, TiptapEditor: true } },
    })

    const counter = wrapper.get('button[aria-label="2 sources"]')
    expect(counter.text()).toBe('2')
    expect(counter.attributes('tabindex')).toBeUndefined()
  })
})

describe('WorkflowContent highlight popup', () => {
  // The popup is teleported to the page body, outside the mounted wrapper.
  afterEach(() => {
    document.body.innerHTML = ''
  })

  const highlight = async (wrapper: ReturnType<typeof mount>) => {
    const text = wrapper.get('.workflow-content-text').element
    const range = document.createRange()
    range.selectNodeContents(text)
    range.getBoundingClientRect = () => ({ top: 100, left: 10, width: 50 }) as DOMRect
    const selection = window.getSelection()!
    selection.removeAllRanges()
    selection.addRange(range)
    document.dispatchEvent(new MouseEvent('mouseup'))
    await nextTick()
  }

  it('offers Feedback beside Explore on the report page, and opens it with the highlighted text', async () => {
    const openFeedback = vi.fn()
    const wrapper = mount(WorkflowContent, {
      props: { elementId: 'p1', workflowId: 'run-1', replacementTag: 'summary', content: '<p>Revenue was 12m</p>' },
      global: { plugins: [i18n], stubs: { AiPanel: true, TiptapEditor: true }, provide: { [OPEN_REPORT_FEEDBACK_KEY as symbol]: openFeedback } },
      attachTo: document.body,
    })

    await highlight(wrapper)
    const buttons = [...document.querySelectorAll('.selection-explore-button button')].map(button => button.textContent?.trim())
    expect(buttons).toEqual(['Explore', 'Feedback'])

    ;(document.querySelector('[data-testid="selection-feedback-button"]') as HTMLButtonElement).click()
    expect(openFeedback).toHaveBeenCalledWith('Revenue was 12m')
    wrapper.unmount()
  })

  const exploreWithSelection = async (selected: string) => {
    const outside = document.body.appendChild(
      Object.assign(document.createElement('p'), { className: 'outside-paragraph', textContent: 'Unrelated' }),
    )
    const wrapper = mount(WorkflowContent, {
      props: { elementId: 'p1', workflowId: 'run-1', replacementTag: 'summary', content: '<p>Revenue was 12m</p>' },
      global: { plugins: [i18n], stubs: { AiPanel: true, TiptapEditor: true } },
      attachTo: document.body,
    })
    const range = document.createRange()
    range.selectNodeContents(document.querySelector(selected)!)
    window.getSelection()!.removeAllRanges()
    window.getSelection()!.addRange(range)

    await wrapper.get('button[aria-label="Explore Analysis"]').trigger('click')

    const question = wrapper.findComponent({ name: 'AiPanel' }).props('initialQuestion') as string
    wrapper.unmount()
    outside.remove()
    return question
  }

  it('asks Explore Analysis about a passage selected in its paragraph without the mouse', async () => {
    expect(await exploreWithSelection('.workflow-content-text')).toContain('Revenue was 12m')
  })

  it('asks Explore Analysis no question about a passage selected elsewhere on the page', async () => {
    expect(await exploreWithSelection('.outside-paragraph')).toBe('')
  })

  it('offers no Feedback where the report page does not provide it', async () => {
    const wrapper = mount(WorkflowContent, {
      props: { elementId: 'p1', workflowId: 'run-1', replacementTag: 'summary', content: '<p>Revenue was 12m</p>' },
      global: { plugins: [i18n], stubs: { AiPanel: true, TiptapEditor: true } },
      attachTo: document.body,
    })

    expect(document.querySelectorAll('.selection-explore-button').length).toBe(0)
    await highlight(wrapper)
    expect(document.querySelectorAll('.selection-explore-button').length).toBe(1)
    expect(document.querySelector('[data-testid="selection-feedback-button"]')).toBeNull()
    wrapper.unmount()
  })
})
