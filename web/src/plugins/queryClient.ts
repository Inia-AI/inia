import { QueryClient } from '@tanstack/vue-query'
import { retryUnlessClientError } from '@/lib/queryRetry'

export const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: retryUnlessClientError } },
})
