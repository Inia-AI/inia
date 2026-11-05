import type { RouteLocationRaw } from 'vue-router'

import { WorkflowOutputRouteAction } from '@/enums'

export const documentDetailsRoute = (id: string): RouteLocationRaw => ({
  name: 'DocumentDetails',
  params: { id },
})

export const reportRoute = (id: string): RouteLocationRaw => ({
  name: 'WorkflowDetails',
  params: { id, action: WorkflowOutputRouteAction.Edit },
})
