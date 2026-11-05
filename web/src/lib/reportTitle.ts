export interface TitledReport {
  title?: string | null
  workflowTypeName?: string | null
}

export function reportTitle(report: TitledReport): string | undefined {
  return report.title || report.workflowTypeName || undefined
}
