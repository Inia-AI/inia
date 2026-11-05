export interface RefinementTurn {
  role: 'user' | 'assistant'
  content: string
}

export interface RefineContentRequest {
  content: string
  instruction: string
  conversationHistory: RefinementTurn[]
}

export interface RefineContentResponse {
  refinedContent: string | null
  explanation?: string | null
  isOutOfScope?: boolean
}
