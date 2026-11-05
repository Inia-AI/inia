import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'

import useCreateWorkflowPrefillStore from './createWorkflowPrefill'
import type { WorkflowReRunPrefill } from '@/lib/workflowReRunPrefill'

const samplePrefill: WorkflowReRunPrefill = {
  workflowId: 'wf-1',
  workflowTypeId: 'type-1',
  counterpartyId: 'cp-1',
  variables: { foo: 'bar' },
  documents: [
    { fileId: 'f-1', name: 'a', extension: '.pdf', documentClass: 'X' },
  ],
}

beforeEach(() => {
  setActivePinia(createPinia())
})

describe('createWorkflowPrefill store', () => {
  it('starts with no pending prefill', () => {
    const store = useCreateWorkflowPrefillStore()
    expect(store.pending).toBeNull()
  })

  it('set() captures the prefill so consume() can hand it back once', () => {
    const store = useCreateWorkflowPrefillStore()
    store.set(samplePrefill)

    expect(store.pending).toEqual(samplePrefill)
  })

  it('consume() returns the pending prefill and clears it', () => {
    const store = useCreateWorkflowPrefillStore()
    store.set(samplePrefill)

    const first = store.consume()
    expect(first).toEqual(samplePrefill)
    expect(store.pending).toBeNull()

    const second = store.consume()
    expect(second).toBeNull()
  })

  it('consume() with nothing pending returns null without throwing', () => {
    const store = useCreateWorkflowPrefillStore()
    expect(store.consume()).toBeNull()
  })

  it('set() overwrites any previous pending prefill', () => {
    const store = useCreateWorkflowPrefillStore()
    store.set(samplePrefill)
    store.set({ ...samplePrefill, workflowId: 'wf-2' })

    expect(store.pending?.workflowId).toBe('wf-2')
  })
})
