export const ColorSchemes = {
  default: ['#3B82F6', '#10B981', '#F59E0B', '#8B5CF6', '#EC4899', '#06B6D4'],
  professional: ['#1e40af', '#059669', '#d97706', '#7c3aed', '#be185d', '#0891b2'],
  vibrant: ['#ef4444', '#22c55e', '#f59e0b', '#a855f7', '#ec4899', '#06b6d4'],
  earth: ['#78716c', '#a3a3a3', '#d6d3d1', '#525252', '#737373', '#a8a29e']
}

export type ColorScheme = keyof typeof ColorSchemes
