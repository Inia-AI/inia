import { z } from 'zod'
import { CHART_TYPES } from './chartTypes'

// Dynamic Chart Block configuration schema matching backend output
export const ChartConfigSchema = z.object({
  type: z.enum(CHART_TYPES),
  title: z.string(),
  data: z.array(z.record(z.string(), z.unknown())), // Flexible data structure
  xAxis: z.object({
    field: z.string(),
    label: z.string().optional()
  }),
  yAxis: z.object({
    field: z.string(),
    label: z.string().optional()
  }),
  series: z.array(z.object({
    name: z.string(),
    data: z.array(z.unknown()),
    color: z.string().optional()
  })).optional(),
  options: z.object({
    seriesGroupField: z.string().optional(),
    colorScheme: z.enum(['default', 'professional', 'vibrant', 'earth']).optional(),
    chartHeight: z.string().optional(),
    chartWidth: z.string().optional(),
    showLegend: z.boolean().optional(),
    showGridLines: z.boolean().optional(),
    enableAnimations: z.boolean().optional(),
    enableExport: z.boolean().optional(),
    stacking: z.enum(['none', 'normal', 'percent']).optional()
  }).optional(),
  metadata: z.object({
    dataPoints: z.number(),
    generatedAt: z.string().optional(),
    currency: z.string().optional(),
    unit: z.string().optional()
  }).optional(),
  layout: z.object({
    width: z.string().optional(),
    height: z.string().optional()
  }).optional()
})

// Color scheme definitions live in a zod-free module so the standalone chart
// builder bundle (shared with the backend Word export) does not pull in zod.
export { ColorSchemes, type ColorScheme } from './colorSchemes'

// Export types
export type ChartConfig = z.infer<typeof ChartConfigSchema>
export type ChartStackingMode = 'none' | 'normal' | 'percent'
