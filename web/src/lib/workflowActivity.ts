import {
  WORKFLOW_ACTIVITY_SCHEMA_VERSION,
  type WorkflowActivity,
} from '@/api/workflowReports/schema'

export interface WorkflowActivityView {
  activeLabels: string[]
  doneLabels: string[]
  doneCount: number
  stepCount: number
}

export const activityViewOf = (
  activity: WorkflowActivity | null | undefined,
): WorkflowActivityView | null => {
  if (
    !activity ||
    activity.schemaVersion !== WORKFLOW_ACTIVITY_SCHEMA_VERSION ||
    !Array.isArray(activity.steps)
  ) {
    return null
  }
  const activeLabels = activity.steps
    .filter(step => step?.status === 'Active' && step.label)
    .map(step => step.label)
  if (activeLabels.length > 0) {
    const doneLabels = activity.steps
      .filter(step => step?.status === 'Done' && step.label)
      .map(step => step.label)
    return {
      activeLabels,
      doneLabels,
      doneCount: doneLabels.length,
      stepCount: activity.steps.length,
    }
  }
  const step = activity.steps[activity.currentStepIndex]
  if (!step?.label) {
    return null
  }
  return {
    activeLabels: [step.label],
    doneLabels: activity.steps
      .slice(0, Math.max(0, activity.currentStepIndex))
      .filter(earlier => earlier?.label)
      .map(earlier => earlier.label),
    doneCount: Math.max(0, Math.min(activity.currentStepIndex, activity.steps.length)),
    stepCount: activity.steps.length,
  }
}
