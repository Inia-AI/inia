/**
 * Shared formatting utilities for token usage display.
 * Used across TokenUsageAnalytics, UsagePanel, and related components.
 */

/**
 * Formats a number with K/M suffixes for large values.
 * @example formatNumber(1500) // returns "1.5K"
 * @example formatNumber(1500000) // returns "1.5M"
 */
export function formatNumber(value: number): string {
  if (value >= 1_000_000) {
    return `${(value / 1_000_000).toFixed(1)}M`
  } else if (value >= 1_000) {
    return `${(value / 1_000).toFixed(1)}K`
  }
  return value.toLocaleString()
}

/**
 * Formats a number as USD currency with 2-4 decimal places.
 * @example formatCurrency(1.5) // returns "$1.50"
 */
export function formatCurrency(value: number): string {
  return USD.format(value)
}

const USD = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD',
  minimumFractionDigits: 2,
  maximumFractionDigits: 4,
})

/**
 * Returns Tailwind CSS classes for usage type badges based on the usage type.
 */
export function getUsageTypeColor(usageType: string): string {
  const colors: Record<string, string> = {
    LLM: 'bg-blue-100 text-blue-800',
    Embedding: 'bg-purple-100 text-purple-800',
    ImageGeneration: 'bg-pink-100 text-pink-800',
    ImageAnalysis: 'bg-orange-100 text-orange-800',
    AudioTranscription: 'bg-green-100 text-green-800',
    AudioGeneration: 'bg-teal-100 text-teal-800',
  }
  return colors[usageType] || 'bg-gray-100 text-gray-800'
}

/**
 * Maps period keys (both camelCase and PascalCase) to display labels.
 * Handles both 'today'/'Today', 'thisWeek'/'ThisWeek', etc.
 */
export function getPeriodLabel(period: string): string {
  const labels: Record<string, string> = {
    today: 'Today',
    Today: 'Today',
    thisWeek: 'This Week',
    ThisWeek: 'This Week',
    thisMonth: 'This Month',
    ThisMonth: 'This Month',
    allTime: 'All Time',
    AllTime: 'All Time',
  }
  return labels[period] || period
}
