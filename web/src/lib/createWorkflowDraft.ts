import { z } from 'zod'
import type { CreateWorkflowPrefill } from './workflowReRunPrefill'

const DRAFT_KEY = 'inia.createWorkflowDraft'

const draftSchema = z.object({
  workflowId: z.string(),
  workflowTypeId: z.string(),
  counterpartyId: z.string().optional(),
  peerCompanyIds: z.array(z.string()).optional(),
  variables: z.record(z.string(), z.unknown()),
  documents: z.array(z.object({ fileId: z.string(), documentClass: z.string().optional(), name: z.string(), extension: z.string() })),
  startStep: z.enum(['documents', 'details', 'review']).optional(),
})

/** Keeps the new report form for this tab, so it comes back after the page is left for another site and reopened. */
export function keepCreateWorkflowDraft(draft: CreateWorkflowPrefill): void {
  sessionStorage.setItem(DRAFT_KEY, JSON.stringify(draft))
}

/** The kept form, once: reading it forgets it. */
export function takeCreateWorkflowDraft(): CreateWorkflowPrefill | null {
  const stored = sessionStorage.getItem(DRAFT_KEY)
  sessionStorage.removeItem(DRAFT_KEY)
  if (stored === null) {
    return null
  }
  try {
    const parsed = draftSchema.safeParse(JSON.parse(stored))
    return parsed.success ? parsed.data : null
  } catch {
    return null
  }
}
