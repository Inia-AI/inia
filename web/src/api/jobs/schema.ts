import { BackgroundJobState, BackgroundJobType } from '@/enums'

export interface Job {
  id: string
  state: BackgroundJobState
  type: BackgroundJobType
  error: string | null
  createdAt: string
  completedAt: string | null
  userId: string
}
