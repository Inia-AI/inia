import { z } from 'zod'

// Request schema
export const GraphQueryRequestSchema = z.object({
  targetTag: z.string().min(1),
  question: z.string().min(1).max(5000),
  startingDepth: z.number().int().min(1).max(6).optional().default(4),
  includePreviews: z.boolean().optional().default(true),
})

export type GraphQueryRequest = z.infer<typeof GraphQueryRequestSchema>

// Response schema
export const GraphQueryResponseSchema = z.object({
  action: z.string().optional(),
  reason: z.string().optional(),
  answer: z.string(),
})

export type GraphQueryResponse = z.infer<typeof GraphQueryResponseSchema>

// Validation response schema
export const ValidateTargetResponseSchema = z.object({
  exists: z.boolean(),
  targetTag: z.string(),
  runId: z.string(),
})

export type ValidateTargetResponse = z.infer<typeof ValidateTargetResponseSchema>
