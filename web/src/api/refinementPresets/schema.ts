export interface RefinementPresetResponse {
  id: string
  name: string
  instruction: string
  isShared: boolean
  userId: string
  createdAt: string
}

export interface CreateRefinementPresetRequest {
  name: string
  instruction: string
  isShared?: boolean
}

export interface UpdateRefinementPresetRequest {
  name: string
  instruction: string
  isShared?: boolean
}
