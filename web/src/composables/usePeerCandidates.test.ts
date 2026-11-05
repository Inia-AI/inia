import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h, ref, type Ref } from 'vue'
import type { PeerCandidate } from '@/api/peerCandidates/schema'

const candidatesRef = ref<PeerCandidate[] | undefined>(undefined)
const lastQueryParams = ref<unknown>(undefined)
const lastEnabled = ref<unknown>(undefined)

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) =>
      params ? `${key} ${JSON.stringify(params)}` : key,
  }),
}))

vi.mock('@/api', () => ({
  default: {
    PeerCandidates: {
      useGetPeerCandidates: (
        params: Ref<unknown>,
        options?: { enabled?: unknown },
      ) => {
        lastQueryParams.value = params
        lastEnabled.value = options?.enabled
        return { data: candidatesRef, isError: ref(false) }
      },
    },
  },
}))

import { usePeerCandidates } from './usePeerCandidates'

const candidate = (overrides: Partial<PeerCandidate> = {}): PeerCandidate => ({
  id: 'c-1',
  name: 'Acme',
  nameVariants: '',
  isEligible: true,
  availability: 'Available',
  ...overrides,
})

const harness = (args: {
  workflowTypeId?: string
  excludeCounterpartyId?: string
  selectedIds?: string[]
  knownCounterparties?: { label: string; value: string }[]
}) => {
  const excludeCounterpartyId = ref(args.excludeCounterpartyId)
  const selectedIds = ref(args.selectedIds ?? [])
  let api: ReturnType<typeof usePeerCandidates> | undefined

  const wrapper = mount(
    defineComponent({
      setup() {
        api = usePeerCandidates({
          workflowTypeId: ref(args.workflowTypeId),
          excludeCounterpartyId,
          selectedIds,
          enabled: ref(true),
          knownCounterparties: ref(args.knownCounterparties ?? []),
        })
        return () => h('div')
      },
    }),
  )

  return { wrapper, api: api!, selectedIds, excludeCounterpartyId }
}

describe('usePeerCandidates', () => {
  beforeEach(() => {
    candidatesRef.value = undefined
    lastQueryParams.value = undefined
  })

  it('asks the API for the candidates of the workflow the peers are chosen for', () => {
    harness({ workflowTypeId: 'wf-1' })

    expect((lastQueryParams.value as Ref<unknown>).value).toEqual({
      workflowTypeId: 'wf-1',
    })
  })

  it('turns the candidates into options carrying their verdict', () => {
    candidatesRef.value = [
      candidate(),
      candidate({ id: 'c-2', name: 'Globex', isEligible: false, availability: 'NotAnalysed' }),
    ]

    const { api } = harness({})

    expect(api.options.value).toHaveLength(2)
    expect(api.options.value[1]!.unavailableReason).toBe(
      'shared.components.peerCompanySelect.notAnalysed',
    )
  })

  it('does not offer the target counterparty as its own peer', () => {
    candidatesRef.value = [candidate({ id: 'target' }), candidate({ id: 'other' })]

    const { api } = harness({ excludeCounterpartyId: 'target' })

    expect(api.options.value.map(o => o.value)).toEqual(['other'])
  })

  it('keeps a selected peer in the list after the candidates page moved past it', async () => {
    candidatesRef.value = [candidate({ id: 'c-1', name: 'Acme' })]

    const { api, selectedIds, wrapper } = harness({ selectedIds: ['c-1'] })
    await wrapper.vm.$nextTick()

    candidatesRef.value = [candidate({ id: 'c-9', name: 'Zeta' })]
    await wrapper.vm.$nextTick()

    expect(api.options.value.map(o => o.value)).toEqual(['c-9', 'c-1'])
    expect(selectedIds.value).toEqual(['c-1'])
  })

  it('drops a remembered peer once it is no longer selected', async () => {
    candidatesRef.value = [candidate({ id: 'c-1' })]

    const { api, selectedIds, wrapper } = harness({ selectedIds: ['c-1'] })
    await wrapper.vm.$nextTick()

    candidatesRef.value = [candidate({ id: 'c-9' })]
    selectedIds.value = []
    await wrapper.vm.$nextTick()

    expect(api.options.value.map(o => o.value)).toEqual(['c-9'])
  })

  it('names a selected peer the candidates page has never carried', () => {
    candidatesRef.value = [candidate({ id: 'c-9', name: 'Zeta' })]

    const { api } = harness({
      selectedIds: ['c-1'],
      knownCounterparties: [{ label: 'Acme', value: 'c-1' }],
    })

    const selected = api.options.value.find(o => o.value === 'c-1')
    expect(selected?.label).toBe('Acme')
  })

  it('prefers the candidate’s own verdict over the fallback name', () => {
    candidatesRef.value = [
      candidate({ id: 'c-1', name: 'Acme', isEligible: false, availability: 'Unreadable' }),
    ]

    const { api } = harness({
      selectedIds: ['c-1'],
      knownCounterparties: [{ label: 'Stale name', value: 'c-1' }],
    })

    const selected = api.options.value.find(o => o.value === 'c-1')
    expect(selected?.label).toBe('Acme')
    expect(selected?.unavailableReason).toBe(
      'shared.components.peerCompanySelect.unreadable',
    )
  })

  it('does not fall back to a name for a peer nobody selected', () => {
    candidatesRef.value = []

    const { api } = harness({
      selectedIds: [],
      knownCounterparties: [{ label: 'Acme', value: 'c-1' }],
    })

    expect(api.options.value).toEqual([])
  })
})

describe('usePeerCandidates search', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    candidatesRef.value = undefined
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  const paramsNow = () => (lastQueryParams.value as Ref<unknown>).value

  it('waits for typing to settle before narrowing the query', async () => {
    const { api } = harness({ workflowTypeId: 'wf-1' })

    api.search.value = 'ac'
    await vi.advanceTimersByTimeAsync(299)
    expect(paramsNow()).toEqual({ workflowTypeId: 'wf-1' })

    await vi.advanceTimersByTimeAsync(1)
    expect(paramsNow()).toEqual({ workflowTypeId: 'wf-1', search: 'ac' })
  })

  it('sends one query for a burst of keystrokes, not one each', async () => {
    const { api } = harness({ workflowTypeId: 'wf-1' })

    for (const term of ['a', 'ac', 'acm', 'acme']) {
      api.search.value = term
      await vi.advanceTimersByTimeAsync(50)
    }
    await vi.advanceTimersByTimeAsync(300)

    expect(paramsNow()).toEqual({ workflowTypeId: 'wf-1', search: 'acme' })
  })

  it('trims the search term', async () => {
    const { api } = harness({})

    api.search.value = '  acme  '
    await vi.advanceTimersByTimeAsync(300)

    expect(paramsNow()).toEqual({ search: 'acme' })
  })

  it('keeps the search inside the length the server accepts', async () => {
    const { api } = harness({})

    api.search.value = 'a'.repeat(400)
    await vi.advanceTimersByTimeAsync(300)

    expect((paramsNow() as { search: string }).search).toHaveLength(250)
  })
})
