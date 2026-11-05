import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import { createI18n } from 'vue-i18n'
import { RouterView, createRouter, createWebHistory, type Router } from 'vue-router'
import { EN } from '@/assets/locales/en'

vi.mock('@/api', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  return {
    default: {
      Documents: {
        useGetDocumentById: () => ({
          data: vueRef({ id: 'doc-1', name: 'annual-report', extension: '.pdf', mediaType: 'ApplicationPdf', pageCount: 1, sources: [] }),
          isLoading: vueRef(false),
          isError: vueRef(false),
          error: vueRef(null),
        }),
        useDownloadDocumentText: () => ({ mutateAsync: vi.fn(), isPending: vueRef(false) }),
      },
      DocumentCategories: { useGetDocumentCategories: () => ({ data: vueRef([]) }) },
      Users: { useGetUsers: () => ({ data: vueRef({ value: [] }) }) },
      Files: { useGetFile: () => ({ mutateAsync: vi.fn() }) },
    },
  }
})

import DocumentDetails from './DocumentDetails.vue'

const DocumentsList = defineComponent({ name: 'DocumentsList', render: () => h('p', 'Documents list') })

let router: Router | null = null

const openDocumentPage = async (arrivedFrom: string) => {
  router = createRouter({
    history: createWebHistory(),
    routes: [
      { path: '/documents', name: 'Documents', component: DocumentsList },
      { path: '/documents/:id', name: 'DocumentDetails', component: DocumentDetails },
    ],
  })
  if (arrivedFrom.startsWith('/documents')) {
    await router.push(arrivedFrom)
  }
  await router.push('/documents/doc-1')
  const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false, fallbackWarn: false })
  const wrapper = mount(defineComponent({ render: () => h(RouterView) }), {
    global: { plugins: [router, i18n], stubs: { DocumentPageReader: true, FilePreviewDialog: true } },
  })
  await flushPromises()
  return wrapper
}

const pressBack = (wrapper: Awaited<ReturnType<typeof openDocumentPage>>) =>
  wrapper.findAll('button').find(button => button.text() === EN.page.documents.detail.back)!.trigger('click')

afterEach(() => {
  router?.options.history.destroy()
  router = null
})

describe('DocumentDetails Back', () => {
  it('returns to the documents list it was opened from, with that list’s filters', async () => {
    window.history.replaceState(null, '', '/')
    const wrapper = await openDocumentPage('/documents?jobId=job-1')

    await pressBack(wrapper)

    await vi.waitFor(() => expect(router!.currentRoute.value.fullPath).toBe('/documents?jobId=job-1'))
  })

  it('goes to the documents list, and stays in the app, when the page was opened from another site', async () => {
    window.history.replaceState(null, '', '/')
    window.history.pushState(null, '', '/a-page-of-another-site')
    const wrapper = await openDocumentPage('another site')

    await pressBack(wrapper)
    await flushPromises()

    expect(router!.currentRoute.value.name).toBe('Documents')
  })
})
