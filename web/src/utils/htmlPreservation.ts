const SPECIAL_ELEMENT_SELECTOR =
  'table, img, canvas, svg, code, iframe, video, audio, script, style, a, fact, fact_source'

/**
 * Replaces special HTML elements (citations, tables, images, etc.) with
 * placeholders so AI services can process text without mangling them.
 * Line breaks and rules are left in place for the model to handle.
 * Returns the cleaned content and a map to restore originals.
 */
export function replaceSpecialElements(html: string): {
  content: string
  replacementMap: Map<string, string>
} {
  const container = new DOMParser().parseFromString(html, 'text/html').body

  const replacementMap = new Map<string, string>()
  const placeholderCounter: Record<string, number> = {}

  const specialElements = Array.from(
    container.querySelectorAll(SPECIAL_ELEMENT_SELECTOR),
  )

  // Filter out nested elements — only replace top-level ones
  const specialSet = new Set(specialElements)
  const topLevelElements = specialElements.filter(el => {
    let parent = el.parentElement
    while (parent && parent !== container) {
      if (specialSet.has(parent)) {
        return false
      }
      parent = parent.parentElement
    }
    return true
  })

  topLevelElements.forEach(el => {
    const type = el.tagName.toLowerCase()
    if (!placeholderCounter[type]) {
      placeholderCounter[type] = 0
    }
    placeholderCounter[type]++
    const placeholderTag = `{{#${type.toUpperCase()}_${placeholderCounter[type]}}}`

    replacementMap.set(placeholderTag, el.outerHTML)

    const placeholder = document.createTextNode(placeholderTag)
    el.parentNode!.replaceChild(placeholder, el)
  })

  return {
    content: container.innerHTML,
    replacementMap,
  }
}

/**
 * Restores original HTML elements from placeholders.
 */
export function restoreSpecialElements(
  content: string,
  replacementMap: Map<string, string>,
): string {
  let result = content
  replacementMap.forEach((originalHtml, placeholder) => {
    result = result.replaceAll(placeholder, originalHtml)
  })

  // Clean up any leftover placeholders the AI may have garbled
  result = result.replace(/\{\{#[A-Z_]+\d+\}\}/g, '')

  return result
}
