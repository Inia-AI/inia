type Translate = (key: string, named?: Record<string, unknown>) => string

export const WRITTEN_REFUSAL_CATEGORY = 'written'

export const refusalReason = (t: Translate, category?: string | null): string => {
  if (!category) {
    return ''
  }

  return category === WRITTEN_REFUSAL_CATEGORY
    ? t('shared.modelRefusal.writtenReason')
    : t('shared.modelRefusal.categoryReason', { category })
}
