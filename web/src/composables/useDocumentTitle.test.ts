import { describe, expect, it } from 'vitest'
import { defineComponent, h, ref, type Ref } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter, RouterView, type Router } from 'vue-router'
import { useDocumentTitle, useItemTitle } from './useDocumentTitle'

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  messages: { en: { page: { documents: { title: 'Documents' }, logs: { title: 'Execution Logs' } } } },
})

const itemName: Ref<string | undefined> = ref()

const Blank = { render: () => h('div') }

const itemPage = () =>
  defineComponent({
    setup() {
      useItemTitle(itemName)
      return () => h('div')
    },
  })

const Shell = defineComponent({
  setup() {
    useDocumentTitle()
    return () => h(RouterView)
  },
})

async function openAt(path: string): Promise<Router> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/documents', component: Blank, meta: { title: 'page.documents.title' } },
      { path: '/documents/:id', component: itemPage(), meta: { title: 'page.documents.title' } },
      { path: '/documents/:id/logs', component: itemPage(), meta: { title: 'page.logs.title' } },
      { path: '/untitled', component: Blank },
    ],
  })
  await router.push(path)
  await router.isReady()
  mount(Shell, { global: { plugins: [router, i18n] } })
  await flushPromises()
  return router
}

describe('the browser tab title', () => {
  it('names the page, then the app', async () => {
    await openAt('/documents')

    expect(document.title).toBe('Documents · Inia AI')
  })

  it('falls back to the app name on a page with no name, never an empty or stale title', async () => {
    const router = await openAt('/documents')

    await router.push('/untitled')
    await flushPromises()

    expect(document.title).toBe('Inia AI')
  })

  it("puts the item's name first on a page that shows one item, once it has loaded", async () => {
    itemName.value = undefined
    await openAt('/documents/d1')

    expect(document.title).toBe('Documents · Inia AI')

    itemName.value = 'Annual report 2025'
    await flushPromises()

    expect(document.title).toBe('Annual report 2025 · Documents · Inia AI')
  })

  it("drops the item's name on leaving its page", async () => {
    itemName.value = 'Annual report 2025'
    const router = await openAt('/documents/d1')

    await router.push('/documents')
    await flushPromises()

    expect(document.title).toBe('Documents · Inia AI')
  })

  it("keeps the item's name when another page of the same item replaces its page", async () => {
    itemName.value = 'Annual report 2025'
    const router = await openAt('/documents/d1')

    await router.push('/documents/d1/logs')
    await flushPromises()

    expect(document.title).toBe('Annual report 2025 · Execution Logs · Inia AI')
  })
})
