import { describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { WorkflowSignaturesDto } from '@/api/signatures/schema'

const { categoriesRef, signaturesRef } = vi.hoisted(() => ({
  categoriesRef: { value: null as unknown },
  signaturesRef: { value: null as unknown },
}))

vi.mock('@/api', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  categoriesRef.value = vueRef([])
  signaturesRef.value = vueRef(null)
  const mutation = () => ({ mutateAsync: vi.fn(), reset: vi.fn(), isPending: vueRef(false) })

  return {
    default: {
      WorkflowCategories: {
        useGetWorkflowCategorySummaries: () => ({
          data: categoriesRef.value,
          isLoading: vueRef(false),
          isError: vueRef(false),
        }),
      },
      Signatures: {
        useGetWorkflowSignatures: () => ({
          data: signaturesRef.value,
          isLoading: vueRef(false),
          isError: vueRef(false),
          error: vueRef(null),
          refetch: vi.fn(),
        }),
        useSubmitPendingMutation: mutation,
        useCastVoteMutation: mutation,
        useAcceptSuggestionMutation: mutation,
        useCancelPendingMutation: mutation,
        useWithdrawVoteMutation: mutation,
      },
      Users: {
        useGetUsers: () => ({ data: vueRef({ value: [] }) }),
      },
    },
  }
})

vi.mock('@/composables/useMyReviewCounts', async () => {
  const { computed } = await vi.importActual<typeof import('vue')>('vue')
  return {
    useMyReviewCounts: () => ({
      countsByWorkflowType: computed(() => new Map()),
      totalCount: computed(() => 0),
      proposerCountsByWorkflowType: computed(() => new Map()),
      proposerTotalCount: computed(() => 0),
      isLoading: computed(() => false),
    }),
  }
})

vi.mock('vue-i18n', async importOriginal => ({
  ...(await importOriginal<typeof import('vue-i18n')>()),
  useI18n: () => ({ t: (key: string) => key }),
}))

import SignatureSettings from './SignatureSettings.vue'
import { withDocumentTitle } from '@/test/helpers/documentTitle'

const block = (blockId: string, blockName: string) => ({
  blockId,
  blockName,
  blockType: 'AiQuery',
  replacementTag: blockId,
  dependsOn: [],
  fieldNames: [],
})

const SIGNATURES = {
  workflowTypeId: 'CreditMemo',
  workflowName: 'Credit Memo',
  reviewThreshold: 2,
  fields: [],
  sections: [
    { sectionId: 's1', sectionTitle: 'Summary', sectionOrder: 1, blocks: [block('b1', 'Overview'), block('b2', 'Risks')] },
    { sectionId: 's2', sectionTitle: 'Financials', sectionOrder: 2, blocks: [block('b3', 'Revenue')] },
  ],
} as unknown as WorkflowSignaturesDto

const mountAt = async (path: string) => {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/signatures', name: 'SignaturesPicker', component: SignatureSettings },
      { path: '/signatures/:workflowTypeId', name: 'SignatureSettings', component: SignatureSettings },
    ],
  })
  router.push(path)
  await router.isReady()
  const wrapper = mount(withDocumentTitle(SignatureSettings), { global: { plugins: [router], mocks: { $t: (key: string) => key }, stubs: { SignatureTestDialog: true, SignatureHistoryTab: true } } })
  await flushPromises()
  return { wrapper, router }
}

describe('SignatureSettings keyboard access', () => {
  it('names the workflow in the browser tab', async () => {
    ;(signaturesRef.value as { value: unknown }).value = SIGNATURES
    await mountAt('/signatures/CreditMemo')

    expect(document.title).toBe('Credit Memo · Inia AI')
  })

  it('opens a workflow from the picker through a native button', async () => {
    ;(categoriesRef.value as { value: unknown }).value = [
      { id: 'c1', name: 'Corporate', workflows: [{ workflowTypeId: 'CreditMemo', workflowTypeName: 'Credit Memo' }] },
    ]
    const { wrapper, router } = await mountAt('/signatures')

    const open = wrapper.get('li button.signatures-root__picker-item')
    expect(open.attributes('type')).toBe('button')
    expect(open.text()).toContain('Credit Memo')

    await open.trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.params.workflowTypeId).toBe('CreditMemo')
  })

  it('selects a section and a block through native buttons that mark the current one', async () => {
    ;(signaturesRef.value as { value: unknown }).value = SIGNATURES
    const { wrapper } = await mountAt('/signatures/CreditMemo')

    const sections = () => wrapper.findAll('li button.signatures-editor__section-item')
    const blocks = () => wrapper.findAll('li button.signatures-editor__block-item')
    expect(sections().map(section => section.attributes('type'))).toEqual(['button', 'button'])
    expect(sections().map(section => section.attributes('aria-current'))).toEqual(['true', undefined])
    expect(blocks().map(item => item.attributes('aria-current'))).toEqual(['true', undefined])

    await blocks()[1]!.trigger('click')
    expect(blocks().map(item => item.attributes('aria-current'))).toEqual([undefined, 'true'])

    await sections()[1]!.trigger('click')
    expect(sections().map(section => section.attributes('aria-current'))).toEqual([undefined, 'true'])
    expect(blocks().map(item => item.text())).toEqual([expect.stringContaining('Revenue')])
  })
})
