import { computed } from 'vue'
import API from '@/api'

interface TypedReport {
  workflowTypeId?: string
  workflowTypeName?: string
}

export function useWorkflowTypeNames() {
  const { data: workflowTypeOptions } =
    API.Workflows.useGetWorkflowTypeOptions()
  const typeNames = computed(
    () =>
      new Map(
        (workflowTypeOptions.value ?? []).map(o => [
          o.workflowTypeId,
          o.workflowTypeName,
        ]),
      ),
  )

  // The reports list endpoint sends only the type id; the platform leaves the name to the client.
  const withTypeName = <T extends TypedReport>(report: T): T => ({
    ...report,
    workflowTypeName:
      typeNames.value.get(report.workflowTypeId ?? '') || report.workflowTypeId,
  })

  return { workflowTypeOptions, typeNames, withTypeName }
}
