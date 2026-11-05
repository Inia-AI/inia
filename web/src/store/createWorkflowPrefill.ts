import { defineStore } from 'pinia'
import { ref } from 'vue'

import type { WorkflowReRunPrefill } from '@/lib/workflowReRunPrefill'

/**
 * Hand-off slot between WorkflowDetails ("Edit inputs first" on a re-run) and
 * the /workflows/create page. Single-shot: the create page calls `consume()`
 * once on mount and the slot is cleared so a later, unrelated visit doesn't
 * inherit a stale prefill.
 */
export default defineStore('createWorkflowPrefill', () => {
  const pending = ref<WorkflowReRunPrefill | null>(null)

  const set = (data: WorkflowReRunPrefill) => {
    pending.value = data
  }

  const consume = (): WorkflowReRunPrefill | null => {
    const current = pending.value
    pending.value = null
    return current
  }

  return { pending, set, consume }
})
