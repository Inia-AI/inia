import { describe, expect, it } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import {
  useDocumentsQueryState,
  type UseDocumentsQueryStateReturn,
} from './useDocumentsQueryState'
import { DEFAULT_QUERY_STATE } from '@/lib/documentsQuery'

type Captured = UseDocumentsQueryStateReturn

const mountHost = async (initialPath = '/documents'): Promise<{ captured: Captured; router: Router }> => {
  let captured!: Captured
  const Host = defineComponent({
    setup() {
      captured = useDocumentsQueryState()
      return () => h('div')
    },
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/documents', component: Host }],
  })
  router.push(initialPath)
  await router.isReady()
  mount(Host, { global: { plugins: [router] } })
  await nextTick()
  return { captured, router }
}

describe('useDocumentsQueryState', () => {
  it('returns defaults when the URL has no query params', async () => {
    const { captured } = await mountHost('/documents')
    expect(captured.state.value).toEqual(DEFAULT_QUERY_STATE)
  })

  it('reads jobId, mediaType and page from the URL', async () => {
    const { captured } = await mountHost('/documents?jobId=abc&mediaType=pdf&page=2')
    expect(captured.state.value).toMatchObject({
      jobId: 'abc',
      mediaType: 'pdf',
      page: 2,
    })
  })

  it('writes patched fields to the URL while preserving other fields', async () => {
    const { captured, router } = await mountHost('/documents?jobId=abc')
    await captured.patchState({ mediaType: 'image' })
    await nextTick()
    expect(router.currentRoute.value.query).toMatchObject({
      jobId: 'abc',
      mediaType: 'image',
    })
  })

  it('drops a field from the URL when patched back to its default', async () => {
    const { captured, router } = await mountHost('/documents?mediaType=pdf')
    await captured.patchState({ mediaType: '' })
    await nextTick()
    expect(router.currentRoute.value.query.mediaType).toBeUndefined()
  })

  it('reflects subsequent router navigations into state', async () => {
    const { captured, router } = await mountHost('/documents')
    expect(captured.state.value.jobId).toBe('')
    await router.push('/documents?jobId=xyz')
    await nextTick()
    expect(captured.state.value.jobId).toBe('xyz')
  })
})
