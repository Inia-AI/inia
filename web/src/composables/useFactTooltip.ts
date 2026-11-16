import { onMounted, onUnmounted, watch, type Ref } from 'vue'
import type { Citation, BlockCitationSource, BlockCitedSources } from '@/api/workflows/schema'
import { isDocumentSource, isUrlSource, isBlockSource } from '@/api/workflows/schema'
import { i18n } from '@/plugins'
import {
  aggregateSources,
  citedSourcesOf,
  combineCitedTexts,
  documentSourceLabel,
  drawnFromSources,
  DRAWN_FROM_VISIBLE_COUNT,
  isUnverifiedDocumentSource,
  isWebUrl,
  type AggregatedSource,
} from '@/utils/citationUtils'
import { SOURCE_CHAIN_EVENT, type SourceChainEventDetail } from '@/utils/sourceChainEvents'
import { isPagePreviewOpen, PAGE_PREVIEW_EVENT, type PagePreviewEventDetail } from '@/utils/pagePreviewEvents'
import { useDebounce } from '@/composables/useDebounce'
import { escapeHtml } from '@/lib/escapeHtml'

const t = i18n.global.t

let globalsInitialized = false
let tooltipContainer: HTMLElement | null = null
let citedTextTooltip: HTMLElement | null = null
let currentFact: HTMLElement | null = null
let scrollTicking = false

const readJsonFromAncestor = <T>(element: Element, attribute: string): T | null => {
  const json = element.parentElement?.closest(`[${attribute}]`)?.getAttribute(attribute)
  if (!json) {
    return null
  }
  try {
    return JSON.parse(json) as T
  } catch {
    return null
  }
}

const getCitationsFromAncestor = (element: Element): Citation[] | null =>
  readJsonFromAncestor<Citation[]>(element, 'data-citations')

const getCitedSourcesByBlockFromAncestor = (element: Element): BlockCitedSources[] =>
  readJsonFromAncestor<BlockCitedSources[]>(element, 'data-cited-sources-by-block') ?? []

const FACT_CONTROL_SELECTOR = 'fact[role="button"]'
const FACT_CONTROL_ATTRIBUTES = ['role', 'tabindex', 'aria-label', 'aria-haspopup', 'aria-expanded'] as const

const factTextOf = (fact: Element): string =>
  Array.from(fact.childNodes)
    .filter(node => node.nodeName.toLowerCase() !== 'fact_source')
    .map(node => node.textContent ?? '')
    .join('')
    .trim()

const factControlAttributes = (fact: Element): Record<(typeof FACT_CONTROL_ATTRIBUTES)[number], string> => ({
  role: 'button',
  tabindex: '0',
  'aria-label': t('shared.citations.marker.label', { text: factTextOf(fact) }),
  'aria-haspopup': 'dialog',
  'aria-expanded': String(fact === currentFact),
})

const setFactControl = (fact: Element, isCited: boolean) => {
  fact.classList.toggle('fact-empty', !isCited)
  if (!isCited) {
    FACT_CONTROL_ATTRIBUTES.forEach(attribute => fact.removeAttribute(attribute))
    return
  }
  Object.entries(factControlAttributes(fact)).forEach(([name, value]) => fact.setAttribute(name, value))
}

const decorateFactsInScope = (scope: HTMLElement) => {
  const citedFactIds = new Set<string>()
  const citationsAttr = scope.getAttribute('data-citations')
  if (citationsAttr) {
    try {
      const citations = JSON.parse(citationsAttr) as Citation[]
      citations.filter(c => c.sources.length > 0).forEach(c => citedFactIds.add(c.id))
    } catch {
      /* empty */
    }
  }

  scope.querySelectorAll('fact').forEach(fact => {
    const factId = fact.getAttribute('id')
    if (factId) {
      setFactControl(fact, citedFactIds.has(factId))
    } else {
      const hasValidSource = Array.from(fact.querySelectorAll('fact_source')).some(
        el => (el.getAttribute('label') || '').length > 0,
      )
      setFactControl(fact, hasValidSource)
    }
  })
}

const getTooltipContainer = () => {
  if (!tooltipContainer) {
    tooltipContainer = document.createElement('div')
    tooltipContainer.id = 'fact-tooltip-container'
    tooltipContainer.setAttribute('role', 'dialog')
    tooltipContainer.setAttribute('aria-label', t('shared.citations.marker.popupLabel'))
    document.body.appendChild(tooltipContainer)
  }
  return tooltipContainer
}

const CITED_TEXT_TOOLTIP_ID = 'fact-cited-text-tooltip'

const getCitedTextTooltip = () => {
  if (!citedTextTooltip) {
    citedTextTooltip = document.createElement('div')
    citedTextTooltip.id = CITED_TEXT_TOOLTIP_ID
    document.body.appendChild(citedTextTooltip)
  }
  return citedTextTooltip
}

const showCitedTextTooltip = (target: HTMLElement) => {
  const citedText = target.dataset.citedText
  if (!citedText) {
    return
  }

  const tooltip = getCitedTextTooltip()
  tooltip.textContent = citedText
  placeNextTo(tooltip, target.getBoundingClientRect(), CITED_TEXT_GAP_PX)
}

const hideCitedTextTooltip = () => {
  if (citedTextTooltip) {
    citedTextTooltip.style.opacity = '0'
    citedTextTooltip.style.visibility = 'hidden'
  }
}

interface LegacyParsedSource {
  type: 'document' | 'web' | 'block' | 'unknown'
  label: string
  citedText: string | null
  pageId: string | null
}

const parseLegacySourceElement = (el: Element): LegacyParsedSource => {
  const rawType = el.getAttribute('type')
  const type: LegacyParsedSource['type'] =
    rawType === 'document' || rawType === 'web' || rawType === 'block' ? rawType : 'unknown'
  const label = el.getAttribute('label') || ''
  const pageId = el.getAttribute('page-id')
  const citedText = el.querySelector('cited-text')?.textContent?.trim() || null

  return { type, label, citedText, pageId }
}

const wrapWithCitedTooltip = (html: string, citedText: string | null): string => {
  if (!citedText) {
    return html
  }
  const escapedText = escapeHtml(citedText).replace(/\n/g, '&#10;')
  return `<span class="fact-source-with-cite" data-cited-text="${escapedText}">${html}</span>`
}

const pageButtonHtml = (safeLabel: string, safePageId: string): string =>
  `<button type="button" class="fact-document-link fact-source-link cursor-pointer text-left" aria-haspopup="dialog" data-doc-name="${safeLabel}" data-page-id="${safePageId}">${safeLabel}</button>`

const truncateUrl = (url: string, maxLength = 50): string => {
  if (url.length <= maxLength) {
    return url
  }
  try {
    const parsed = new URL(url)
    const domain = parsed.hostname
    const pathAndQuery = parsed.pathname + parsed.search
    const availableForPath = maxLength - domain.length - 3
    if (availableForPath > 10) {
      return domain + pathAndQuery.substring(0, availableForPath) + '...'
    }
    return url.substring(0, maxLength - 3) + '...'
  } catch {
    return url.substring(0, maxLength - 3) + '...'
  }
}

const citedAddressHtml = (address: string): string => {
  const displayAddress = escapeHtml(truncateUrl(address))
  const safeAddress = escapeHtml(address)
  return isWebUrl(address)
    ? `<a href="${safeAddress}" class="fact-source-link" target="_blank" rel="noopener noreferrer" title="${safeAddress}">${displayAddress}</a>`
    : `<span class="fact-source-text" title="${safeAddress}">${displayAddress}</span>`
}

const formatStructuredSource = (agg: AggregatedSource, citedSourcesByBlock: BlockCitedSources[] = []): string => {
  const source = agg.source
  const combinedCitedText = combineCitedTexts(agg.citedTexts)

  if (isDocumentSource(source)) {
    const safeLabel = escapeHtml(documentSourceLabel(source, 'Document'))
    if (isUnverifiedDocumentSource(source)) {
      const badge = `<span class="fact-unverified-badge" title="${escapeHtml(t('shared.citations.unverified.tooltip'))}">${escapeHtml(t('shared.citations.unverified.badge'))}</span>`
      const item = `<span class="fact-source-bullet">•</span><span class="fact-source-unverified">${safeLabel}</span> ${badge}`
      return wrapWithCitedTooltip(item, combinedCitedText)
    }
    const page = `<span class="fact-source-bullet">•</span>${pageButtonHtml(safeLabel, escapeHtml(source.pageId))}`
    return wrapWithCitedTooltip(page, combinedCitedText)
  } else if (isUrlSource(source)) {
    const address = `<span class="fact-source-bullet">•</span>${citedAddressHtml(source.url)}`
    return wrapWithCitedTooltip(address, combinedCitedText)
  } else if (isBlockSource(source)) {
    return formatBlockSource(source, combinedCitedText, citedSourcesByBlock)
  }
  return escapeHtml(String(source))
}

const SPARKLE_ICON = `<svg class="fact-block-icon" viewBox="0 0 28 24" fill="currentColor">
      <path d="M9 1C9 7 4 12 0 12C4 12 9 17 9 23C9 17 14 12 20 12C14 12 9 7 9 1Z"/>
      <path d="M22 0C22 3 19.5 5 17 5C19.5 5 22 7 22 10C22 7 24.5 5 27 5C24.5 5 22 3 22 0Z" opacity="0.85"/>
    </svg>`

/**
 * A block citation. With the block's list of pages and links, the popup shows that list, the
 * sources that state the fact first, and the block name and trace button under it. A report
 * built before the lists existed keeps the one-line "Synthesized with" form.
 */
const formatBlockSource = (
  source: BlockCitationSource,
  combinedCitedText: string | null,
  citedSourcesByBlock: BlockCitedSources[],
): string => {
  const safeBlockTitle = escapeHtml(source.blockTitle || t('shared.citations.synthesis.defaultTitle'))
  const blockData = escapeHtml(JSON.stringify(source))
  const drawn = drawnFromSources(source, citedSourcesOf(source, citedSourcesByBlock))
  const canTrace = drawn.length > 0 || source.upstreamSources.length > 0
  const traceLabel = canTrace
    ? `${t('shared.citations.synthesis.traceButton')} &rarr;`
    : `${t('shared.citations.synthesis.viewButton')} &rarr;`
  const traceButton = `<button class="fact-trace-btn" data-block-source="${blockData}">${traceLabel}</button>`

  if (drawn.length === 0) {
    const synthesisLabel = t('shared.citations.synthesis.labelWithTitle', { title: safeBlockTitle })
    const blockHtml = `<span class="fact-block-source">
      ${SPARKLE_ICON} ${synthesisLabel}
      ${traceButton}
    </span>`
    return wrapWithCitedTooltip(blockHtml, combinedCitedText)
  }

  const items = drawn
    .map((item, index) => {
      const matchClass = item.matchesFact ? ' is-match' : ''
      const hidden = index >= DRAWN_FROM_VISIBLE_COUNT ? ' hidden' : ''
      return `<li class="fact-drawn-from-item${matchClass}"${hidden}>${formatStructuredSource(item)}</li>`
    })
    .join('')
  const showAll = drawn.length > DRAWN_FROM_VISIBLE_COUNT
    ? `<button type="button" class="fact-show-all-btn">${escapeHtml(t('shared.citations.drawnFrom.showAll', { count: drawn.length }))}</button>`
    : ''
  const footer = wrapWithCitedTooltip(
    `<span class="fact-drawn-from-block">${SPARKLE_ICON}${safeBlockTitle}</span>`,
    combinedCitedText,
  )

  return `<div class="fact-drawn-from">
    <div class="fact-drawn-from-title">${escapeHtml(t('shared.citations.drawnFrom.title'))}</div>
    <ul class="fact-drawn-from-list">${items}</ul>
    ${showAll}
    <div class="fact-drawn-from-footer">${footer}${traceButton}</div>
  </div>`
}

const formatLegacySource = (el: Element): string => {
  const { type, label, citedText, pageId } = parseLegacySourceElement(el)
  const safeLabel = escapeHtml(label)

  switch (type) {
    case 'web': {
      return wrapWithCitedTooltip(citedAddressHtml(label), citedText)
    }
    case 'document': {
      if (pageId) {
        return wrapWithCitedTooltip(pageButtonHtml(safeLabel, escapeHtml(pageId)), citedText)
      }
      const docSpan = `<span class="fact-source-text">${safeLabel}</span>`
      return wrapWithCitedTooltip(docSpan, citedText)
    }
    case 'block': {
      const blockText = `<span class="fact-source-text">Block "${safeLabel}"</span>`
      return wrapWithCitedTooltip(blockText, citedText)
    }
    default: {
      return wrapWithCitedTooltip(safeLabel || 'Unknown source', citedText)
    }
  }
}

const TOOLTIP_GAP_PX = 10
const CITED_TEXT_GAP_PX = 8

/**
 * Puts a floating element above its anchor when it fits there, and otherwise on the side with
 * more room, capped to that room so it scrolls rather than runs off the screen: a block
 * citation's full list, or the excerpt of a row in a popup that opened below its marker, can be
 * taller than the space on either side.
 */
const placeNextTo = (floating: HTMLElement, rect: DOMRect, gapPx: number) => {
  floating.style.visibility = 'visible'
  floating.style.opacity = '1'

  // Measured without lifting the cap, which would scroll a capped list back to its top.
  const fullHeight = floating.scrollHeight + floating.offsetHeight - floating.clientHeight
  const roomAbove = rect.top - gapPx
  const roomBelow = window.innerHeight - rect.bottom - gapPx
  const above = fullHeight <= roomAbove || roomAbove >= roomBelow

  floating.style.maxHeight = `${Math.max(above ? roomAbove : roomBelow, 0)}px`
  floating.style.left = `${rect.left + rect.width / 2}px`
  floating.style.top = above ? `${rect.top - gapPx}px` : `${rect.bottom + gapPx}px`
  floating.style.transform = above ? 'translate(-50%, -100%)' : 'translate(-50%, 0)'
}

const positionTooltip = (tooltip: HTMLElement, rect: DOMRect) => placeNextTo(tooltip, rect, TOOLTIP_GAP_PX)

const showTooltip = (fact: HTMLElement) => {
  const tooltip = getTooltipContainer()
  const rect = fact.getBoundingClientRect()
  const factId = fact.getAttribute('id')

  let listItems = ''

  if (factId) {
    const citations = getCitationsFromAncestor(fact)
    const citation = citations?.find(c => c.id === factId)

    if (citation && citation.sources.length > 0) {
      const citedSourcesByBlock = getCitedSourcesByBlockFromAncestor(fact)
      const aggregated = aggregateSources(citation.sources)
      listItems = aggregated.map(agg => `<li>${formatStructuredSource(agg, citedSourcesByBlock)}</li>`).join('')
    } else {
      tooltip.textContent = 'No sources available'
      positionTooltip(tooltip, rect)
      return
    }
  } else {
    const factSources = Array.from(fact.querySelectorAll('fact_source'))
    if (factSources.length > 0) {
      listItems = factSources.map(el => `<li>${formatLegacySource(el)}</li>`).join('')
    } else {
      tooltip.textContent = 'No sources available'
      positionTooltip(tooltip, rect)
      return
    }
  }

  // eslint-disable-next-line no-restricted-syntax -- the list holds buttons, which sanitizeHtml drops; every value from a citation goes through escapeHtml
  tooltip.innerHTML = `<ul style="margin: 0; padding-left: 0; list-style-type: none;">${listItems}</ul>`
  positionTooltip(tooltip, rect)
}

const hideTooltip = () => {
  if (tooltipContainer) {
    tooltipContainer.style.opacity = '0'
    tooltipContainer.style.visibility = 'hidden'
  }
  if (currentFact?.matches(FACT_CONTROL_SELECTOR)) {
    currentFact.setAttribute('aria-expanded', 'false')
  }
  currentFact = null
}

const factHasSources = (fact: HTMLElement): boolean => {
  const factId = fact.getAttribute('id')
  if (!factId) {
    return fact.querySelector('fact_source') !== null
  }
  const citation = getCitationsFromAncestor(fact)?.find(c => c.id === factId)
  return citation !== undefined && citation.sources.length > 0
}

const toggleFactTooltip = (fact: HTMLElement) => {
  const wasOpen = currentFact === fact
  hideTooltip()
  if (wasOpen) {
    return
  }
  currentFact = fact
  if (fact.matches(FACT_CONTROL_SELECTOR)) {
    fact.setAttribute('aria-expanded', 'true')
  }
  showTooltip(fact)
}

const focusablesIn = (container: HTMLElement): HTMLElement[] =>
  Array.from(container.querySelectorAll<HTMLElement>('a[href], button:not([disabled])')).filter(
    element => !element.closest('[hidden]'),
  )

const returnFocusToFact = () => {
  const fact = currentFact
  hideTooltip()
  fact?.focus()
}

const onFactKeydown = (e: KeyboardEvent) => {
  const target = e.target as HTMLElement
  const tooltip = tooltipContainer

  if (e.key === 'Escape' && currentFact && !isPagePreviewOpen()) {
    if (tooltip?.contains(document.activeElement)) {
      returnFocusToFact()
    } else {
      hideTooltip()
    }
    return
  }

  if ((e.key === 'Enter' || e.key === ' ') && target.matches?.(FACT_CONTROL_SELECTOR)) {
    e.preventDefault()
    if (e.repeat) {
      return
    }
    toggleFactTooltip(target)
    if (currentFact === target) {
      focusablesIn(getTooltipContainer())[0]?.focus()
    }
    return
  }

  // Leaving the popup puts focus back on its marker; a forward Tab is left to run from there,
  // so it lands on whatever follows the marker in the report rather than past the page's end.
  if (e.key === 'Tab' && currentFact && tooltip?.contains(target)) {
    const focusables = focusablesIn(tooltip)
    const leavesForward = !e.shiftKey && target === focusables[focusables.length - 1]
    const leavesBackward = e.shiftKey && target === focusables[0]
    if (leavesForward || leavesBackward) {
      returnFocusToFact()
      if (leavesBackward) {
        e.preventDefault()
      }
    }
  }
}

const ensureGlobalHandlers = () => {
  if (globalsInitialized) {
    return
  }
  globalsInitialized = true

  document.addEventListener('click', e => {
    const target = e.target as HTMLElement
    // A click that closes the page preview reaches the document after the preview has unmounted.
    if (isPagePreviewOpen() || !target.isConnected) {
      return
    }

    const fact = target.closest('fact') as HTMLElement
    const tooltip = getTooltipContainer()

    const pageButton = target.closest<HTMLButtonElement>('.fact-document-link')
    if (pageButton) {
      const title = pageButton.dataset.docName || ''
      const pageId = pageButton.dataset.pageId || ''
      if (pageId) {
        window.dispatchEvent(new CustomEvent<PagePreviewEventDetail>(PAGE_PREVIEW_EVENT, {
          detail: { pageId, title, opener: pageButton, fallback: currentFact ?? undefined },
        }))
      }
      return
    }

    const showAllBtn = target.closest('.fact-show-all-btn') as HTMLButtonElement
    if (showAllBtn) {
      e.preventDefault()
      e.stopPropagation()
      const revealed = Array.from(
        showAllBtn.closest('.fact-drawn-from')?.querySelectorAll<HTMLElement>('.fact-drawn-from-item[hidden]') ?? [],
      )
      revealed.forEach(item => { item.hidden = false })
      if (document.activeElement === showAllBtn && revealed[0]) {
        focusablesIn(revealed[0])[0]?.focus()
      }
      showAllBtn.remove()
      if (currentFact) {
        positionTooltip(tooltip, currentFact.getBoundingClientRect())
      }
      return
    }

    const traceBtn = target.closest('.fact-trace-btn') as HTMLButtonElement
    if (traceBtn) {
      e.preventDefault()
      e.stopPropagation()
      const blockSourceData = traceBtn.dataset.blockSource
      if (blockSourceData) {
        try {
          const blockSource = JSON.parse(blockSourceData) as BlockCitationSource
          const citedSourcesByBlock = currentFact ? getCitedSourcesByBlockFromAncestor(currentFact) : []
          window.dispatchEvent(new CustomEvent<SourceChainEventDetail>(SOURCE_CHAIN_EVENT, {
            detail: {
              blockSource,
              citedSourcesByBlock,
              opener: currentFact ?? undefined,
              fallback: currentFact?.closest<HTMLElement>('[data-citations]') ?? undefined,
            },
          }))
          hideTooltip()
        } catch (err) {
          console.error('Failed to parse block source data:', err)
        }
      }
      return
    }

    if (tooltip.contains(target)) {
      return
    }

    if (fact && factHasSources(fact)) {
      toggleFactTooltip(fact)
      e.stopPropagation()
      return
    }

    if (currentFact) {
      hideTooltip()
    }
  })

  document.addEventListener('keydown', onFactKeydown)

  document.addEventListener(
    'scroll',
    e => {
      if (tooltipContainer?.contains(e.target as Node)) {
        return
      }
      if (!scrollTicking && currentFact) {
        requestAnimationFrame(() => {
          if (currentFact && tooltipContainer) {
            positionTooltip(tooltipContainer, currentFact.getBoundingClientRect())
          }
          scrollTicking = false
        })
        scrollTicking = true
      }
    },
    { capture: true },
  )

  document.addEventListener('mouseover', e => {
    const target = e.target as HTMLElement
    const sourceWithCite = target.closest('.fact-source-with-cite') as HTMLElement
    if (sourceWithCite) {
      showCitedTextTooltip(sourceWithCite)
    }
  })

  document.addEventListener('mouseout', e => {
    const target = e.target as HTMLElement
    const sourceWithCite = target.closest('.fact-source-with-cite') as HTMLElement
    if (sourceWithCite) {
      hideCitedTextTooltip()
    }
  })

  document.addEventListener('focusin', e => {
    const target = e.target as HTMLElement
    const sourceWithCite = target.closest('.fact-source-with-cite') as HTMLElement
    if (sourceWithCite) {
      showCitedTextTooltip(sourceWithCite)
      target.setAttribute('aria-describedby', CITED_TEXT_TOOLTIP_ID)
    }
  })

  document.addEventListener('focusout', e => {
    const target = e.target as HTMLElement
    if (target.closest('.fact-source-with-cite')) {
      hideCitedTextTooltip()
      target.removeAttribute('aria-describedby')
    }
  })
}

export function useFactTooltip(scopeRef: Ref<HTMLElement | null>) {
  let observer: MutationObserver | null = null
  const debouncedDecorateScope = useDebounce(() => {
    if (scopeRef.value) {
      decorateFactsInScope(scopeRef.value)
    }
  }, 100)

  let observedScope: HTMLElement | null = null
  const observeScope = (scope: HTMLElement | null) => {
    if (scope === observedScope) {
      return
    }
    observer?.disconnect()
    observer = null
    observedScope = scope
    if (!scope) {
      return
    }

    observer = new MutationObserver(() => debouncedDecorateScope())
    observer.observe(scope, { childList: true, subtree: true })
    decorateFactsInScope(scope)
  }

  onMounted(() => {
    ensureGlobalHandlers()
    observeScope(scopeRef.value)
  })

  // A paragraph that leaves edit mode renders a new scope element, which needs its own observer.
  watch(scopeRef, observeScope, { flush: 'post' })

  onUnmounted(() => observeScope(null))
}
