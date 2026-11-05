export interface HealthResponse {
  status: 'healthy' | 'unhealthy'
  timestamp: string
  version?: string
  details?: Record<string, unknown>
}
