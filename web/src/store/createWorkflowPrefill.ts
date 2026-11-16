import { defineStore } from 'pinia'
import { ref } from 'vue'

import type { CreateWorkflowPrefill } from '@/lib/workflowReRunPrefill'

/**
 * Hand-off slot between a report and the /workflows/create page: "Edit inputs
 * first" on a re-run, "Change the documents", and "Continue to" the next
 * workflow. Single-shot: the create page calls `consume()` once on mount and
 * the slot is cleared so a later, unrelated visit doesn't inherit a stale
 * prefill.
 */
export default defineStore('createWorkflowPrefill', () => {
  const pending = ref<CreateWorkflowPrefill | null>(null)

  const set = (data: CreateWorkflowPrefill) => {
    pending.value = data
  }

  const consume = (): CreateWorkflowPrefill | null => {
    const current = pending.value
    pending.value = null
    return current
  }

  return { pending, set, consume }
})
