import type { DirectiveBinding, ObjectDirective } from 'vue'

import { renderContent, sanitizeHtml, sanitizeModelHtml } from '@/lib/renderContent'

type SafeHtmlModifier = 'model' | 'markdown'
type SafeHtmlBinding = DirectiveBinding<string | null | undefined, SafeHtmlModifier>

const toSafeHtml = ({ value, modifiers }: SafeHtmlBinding): string => {
  if (!value) {
    return ''
  }
  if (modifiers.markdown) {
    return renderContent(value)
  }
  return modifiers.model ? sanitizeModelHtml(value) : sanitizeHtml(value)
}

export const vSafeHtml: ObjectDirective<HTMLElement, string | null | undefined, SafeHtmlModifier> = {
  beforeMount: (el, binding) => {
    el.innerHTML = toSafeHtml(binding)
  },
  updated: (el, binding) => {
    if (binding.value !== binding.oldValue) {
      el.innerHTML = toSafeHtml(binding)
    }
  },
}
