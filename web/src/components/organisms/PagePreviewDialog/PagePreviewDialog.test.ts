import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'

import api from '@/api'
import { EN } from '@/assets/locales/en'
import PagePreviewDialog from './PagePreviewDialog.vue'

let wrapper: VueWrapper | null = null
let opener: HTMLButtonElement

const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')

async function openPreview(page: { blob: Blob; contentType: string } | Error) {
  const getPageImage = vi.spyOn(api.Pages.service, 'getPageImage')
  if (page instanceof Error) {
    getPageImage.mockRejectedValue(page)
  } else {
    getPageImage.mockResolvedValue(page)
  }
  opener = document.createElement('button')
  opener.textContent = 'Annual Report.pdf - page 81'
  document.body.appendChild(opener)
  opener.focus()

  wrapper = mount(PagePreviewDialog, {
    attachTo: document.body,
    props: {
      open: true,
      pageId: 'page-81',
      title: 'Annual Report.pdf - page 81',
      opener,
      'onUpdate:open': (open: boolean) => wrapper!.setProps({ open }),
    },
    global: { plugins: [createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false })] },
  })
  await flushPromises()
  return getPageImage
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = null
  opener?.remove()
  vi.restoreAllMocks()
  document.body.innerHTML = ''
})

describe('PagePreviewDialog', () => {
  it('opens as a dialog named for the page, hides the page behind it, and puts focus on its named close button', async () => {
    const getPageImage = await openPreview({ blob: new Blob(['page text'], { type: 'text/plain' }), contentType: 'text/plain' })

    expect(getPageImage).toHaveBeenCalledWith('page-81')
    expect(opener.closest('[aria-hidden="true"]')).not.toBeNull()
    const title = document.getElementById(dialog()!.getAttribute('aria-labelledby')!)
    expect(title!.textContent).toContain('Annual Report.pdf - page 81')
    const close = dialog()!.querySelector<HTMLButtonElement>('button')!
    expect(close.getAttribute('type')).toBe('button')
    expect(close.textContent!.trim()).toBe('Close')
    expect(dialog()!.contains(document.activeElement)).toBe(true)
    expect(dialog()!.textContent).toContain('page text')
  })

  it('stacks above the source chain panel it can open from, backdrop included', async () => {
    await openPreview({ blob: new Blob(['page text'], { type: 'text/plain' }), contentType: 'text/plain' })

    const overlay = Array.from(document.body.querySelectorAll<HTMLElement>('[data-state="open"]')).find(
      element => element !== dialog() && !dialog()!.contains(element),
    )!
    expect(dialog()!.classList).toContain('z-[1001]')
    expect(overlay.classList).toContain('z-[1000]')
    expect(overlay.classList).not.toContain('z-50')
  })

  it('says why when the page cannot be loaded', async () => {
    await openPreview(new Error('network'))

    expect(dialog()!.textContent).toContain(EN.shared.citations.errors.loadPreviewFailed)
  })

  it.each([
    ['Escape', () => document.activeElement!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))],
    ['the close button', () => dialog()!.querySelector<HTMLButtonElement>('button')!.click()],
  ])('closes on %s and hands focus back to the control that opened it', async (_, close) => {
    await openPreview({ blob: new Blob(['page text'], { type: 'text/plain' }), contentType: 'text/plain' })

    close()
    await flushPromises()

    expect(wrapper!.emitted('update:open')).toEqual([[false]])
    expect(dialog()).toBeNull()
    expect(document.activeElement).toBe(opener)
  })

  it('names itself by the title alone, with no description id that points nowhere', async () => {
    const warn = vi.spyOn(console, 'warn')
    await openPreview({ blob: new Blob(['page text'], { type: 'text/plain' }), contentType: 'text/plain' })

    expect(dialog()!.hasAttribute('aria-describedby')).toBe(false)
    expect(warn.mock.calls.flat().join(' ')).not.toContain('Missing `Description`')
  })

  it('hands focus to the fallback when the control that opened it is gone', async () => {
    await openPreview({ blob: new Blob(['page text'], { type: 'text/plain' }), contentType: 'text/plain' })
    const fallback = document.createElement('button')
    document.body.appendChild(fallback)
    await wrapper!.setProps({ fallbackFocus: fallback })
    opener.remove()

    dialog()!.querySelector<HTMLButtonElement>('button')!.click()
    await flushPromises()

    expect(document.activeElement).toBe(fallback)
  })
})

describe('PagePreviewDialog page loading order', () => {
  afterEach(() => {
    Reflect.deleteProperty(URL, 'createObjectURL')
    Reflect.deleteProperty(URL, 'revokeObjectURL')
  })

  type PageResponse = { blob: Blob; contentType: string }
  const deferred = () => {
    let resolve!: (page: PageResponse) => void
    const promise = new Promise<PageResponse>(done => {
      resolve = done
    })
    return { promise, resolve }
  }
  const textPage = (text: string): PageResponse => ({ blob: new Blob([text], { type: 'text/plain' }), contentType: 'text/plain' })

  const mountWith = async (pages: Record<string, Promise<PageResponse>>) => {
    vi.spyOn(api.Pages.service, 'getPageImage').mockImplementation(pageId => pages[pageId]!)
    wrapper = mount(PagePreviewDialog, {
      attachTo: document.body,
      props: { open: true, pageId: 'page-a', title: 'Annual Report.pdf' },
      global: { plugins: [createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false })] },
    })
    await flushPromises()
  }

  it('shows the page asked for last when an earlier page answers after it', async () => {
    const slow = deferred()
    const fast = deferred()
    await mountWith({ 'page-a': slow.promise, 'page-b': fast.promise })
    await wrapper!.setProps({ pageId: 'page-b' })

    fast.resolve(textPage('page two'))
    await flushPromises()
    slow.resolve(textPage('page one'))
    await flushPromises()

    expect(dialog()!.textContent).toContain('page two')
    expect(dialog()!.textContent).not.toContain('page one')
  })

  it('keeps loading while the page asked for last is still on its way', async () => {
    const earlier = deferred()
    const later = deferred()
    await mountWith({ 'page-a': earlier.promise, 'page-b': later.promise })
    await wrapper!.setProps({ pageId: 'page-b' })

    earlier.resolve(textPage('page one'))
    await flushPromises()

    expect(dialog()!.textContent).toContain(EN.shared.citations.panel.loading)
    expect(dialog()!.textContent).not.toContain('page one')
  })

  it('makes no image for a page that answers after the preview closed', async () => {
    const createObjectURL = vi.fn(() => 'blob:page')
    Object.defineProperty(URL, 'createObjectURL', { value: createObjectURL, configurable: true })
    Object.defineProperty(URL, 'revokeObjectURL', { value: vi.fn(), configurable: true })
    const page = deferred()
    await mountWith({ 'page-a': page.promise })

    await wrapper!.setProps({ open: false })
    page.resolve({ blob: new Blob(['png'], { type: 'image/png' }), contentType: 'image/png' })
    await flushPromises()

    expect(createObjectURL).not.toHaveBeenCalled()
  })
})

