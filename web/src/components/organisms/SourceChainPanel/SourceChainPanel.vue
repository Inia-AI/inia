<script lang="ts" setup>
import { ref, computed, onMounted, onUnmounted, watch, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { ScrollArea } from '@/components/ui/scroll-area'
import type { BlockCitationSource, BlockCitedSources } from '@/api/workflows/schema'
import { isDocumentSource, isUrlSource, isBlockSource } from '@/api/workflows/schema'
import { SOURCE_CHAIN_EVENT, type SourceChainEventDetail } from '@/utils/sourceChainEvents'
import { PAGE_PREVIEW_EVENT, type PagePreviewEventDetail } from '@/utils/pagePreviewEvents'
import PagePreviewDialog from '@/components/organisms/PagePreviewDialog/PagePreviewDialog.vue'
import { focusFirstConnected } from '@/utils/focus'
import { FocusScope } from 'reka-ui'
import {
  aggregateSources,
  citedSourcesOf,
  documentSourceLabel,
  drawnFromSources,
  DRAWN_FROM_VISIBLE_COUNT,
  isUnverifiedDocumentSource,
  isWebUrl,
  type AggregatedSource,
  type DrawnFromSource,
} from '@/utils/citationUtils'

const { t: $t } = useI18n()

// Props
const props = defineProps<{
  isOpen?: boolean
}>()

// Emits
const emit = defineEmits<{
  (e: 'update:isOpen', value: boolean): void
}>()

// State
const sourceChainStack = ref<BlockCitationSource[]>([])
const preview = ref<{
  open: boolean
  pageId: string | null
  title: string
  opener: HTMLElement | null
  fallback: HTMLElement | null
}>({
  open: false,
  pageId: null,
  title: '',
  opener: null,
  fallback: null,
})
const panelScope = ref<{ $el: HTMLElement } | null>(null)
const panelElement = computed(() => panelScope.value?.$el ?? null)
let panelOpener: HTMLElement | null = null
let panelFallback: HTMLElement | null = null

// Cited text expand/collapse
const citedTextExpanded = ref(false)
const citedTextRef = ref<HTMLParagraphElement | null>(null)
const citedTextOverflows = ref(false)

const checkCitedTextOverflow = async () => {
  await nextTick()
  const el = citedTextRef.value
  if (el) {
    citedTextOverflows.value = el.scrollHeight > el.clientHeight
  } else {
    citedTextOverflows.value = false
  }
}

// Computed
const isVisible = computed(() => sourceChainStack.value.length > 0)

const currentBlock = computed(() => {
  return sourceChainStack.value.at(-1)
})

const currentSources = computed((): AggregatedSource[] => {
  const raw = currentBlock.value?.upstreamSources ?? []
  return aggregateSources(raw)
})

const showAllDrawnFrom = ref(false)
const citedSourcesByBlock = ref<BlockCitedSources[]>([])

const drawnFrom = computed((): DrawnFromSource[] =>
  currentBlock.value
    ? drawnFromSources(currentBlock.value, citedSourcesOf(currentBlock.value, citedSourcesByBlock.value))
    : [],
)

interface SourceSection {
  key: string
  title: string
  total: number
  items: (AggregatedSource & { matchesFact?: boolean })[]
}

/**
 * The upstream blocks, then the pages and links the block drew on. With neither, the upstream
 * section stays, empty, so the panel shows its "No further sources traced" state.
 */
const sourceSections = computed((): SourceSection[] => {
  const sections: SourceSection[] = []
  if (currentSources.value.length > 0 || drawnFrom.value.length === 0) {
    sections.push({
      key: 'synthesized',
      title: $t('shared.citations.panel.synthesizedFrom'),
      total: currentSources.value.length,
      items: currentSources.value,
    })
  }
  if (drawnFrom.value.length > 0) {
    sections.push({
      key: 'drawnFrom',
      title: $t('shared.citations.drawnFrom.title'),
      total: drawnFrom.value.length,
      items: showAllDrawnFrom.value ? drawnFrom.value : drawnFrom.value.slice(0, DRAWN_FROM_VISIBLE_COUNT),
    })
  }
  return sections
})

const breadcrumbs = computed(() => {
  return sourceChainStack.value.map(s => s.blockTitle || s.blockId)
})

// Methods
const openPanel = async (
  blockSource: BlockCitationSource,
  lists: BlockCitedSources[],
  opener?: HTMLElement,
  fallback?: HTMLElement,
) => {
  citedSourcesByBlock.value = lists
  sourceChainStack.value = [blockSource]
  panelOpener = opener ?? null
  panelFallback = fallback ?? null
  emit('update:isOpen', true)
  await nextTick()
  focusFirstConnected(panelElement.value)
}

const closePanel = async () => {
  const opener = panelOpener
  const fallback = panelFallback
  panelOpener = null
  panelFallback = null
  sourceChainStack.value = []
  emit('update:isOpen', false)
  await nextTick()
  focusFirstConnected(opener, fallback)
}

const traceBlockSource = (blockSource: BlockCitationSource) => {
  sourceChainStack.value.push(blockSource)
}

const navigateBack = () => {
  if (sourceChainStack.value.length > 1) {
    sourceChainStack.value.pop()
  } else {
    closePanel()
  }
}

const navigateToBreadcrumb = (index: number) => {
  sourceChainStack.value = sourceChainStack.value.slice(0, index + 1)
}

const openPreview = (pageId: string, title: string, opener: HTMLElement | null, fallback: HTMLElement | null) => {
  preview.value = { open: true, pageId, title, opener, fallback }
}

const handleSourceClick = (agg: AggregatedSource, event: MouseEvent) => {
  const source = agg.source
  if (isDocumentSource(source)) {
    if (isUnverifiedDocumentSource(source)) {
      return
    }
    openPreview(source.pageId, documentSourceLabel(source, 'Document'), event.currentTarget as HTMLElement, panelElement.value)
  } else if (isUrlSource(source)) {
    if (isWebUrl(source.url)) {
      window.open(source.url, '_blank', 'noopener,noreferrer')
    }
  } else if (isBlockSource(source)) {
    traceBlockSource(source)
  }
}

const getSourceIcon = (agg: AggregatedSource): string => {
  const source = agg.source
  if (isDocumentSource(source)) return 'document'
  if (isUrlSource(source)) return 'link'
  if (isBlockSource(source)) return 'block'
  return 'unknown'
}

const getSourceLabel = (agg: AggregatedSource): string => {
  const source = agg.source
  if (isDocumentSource(source)) {
    return documentSourceLabel(source, $t('shared.citations.sources.document'))
  }
  if (isUrlSource(source)) {
    try {
      const url = new URL(source.url)
      return url.hostname + (url.pathname !== '/' ? url.pathname.substring(0, 30) : '')
    } catch {
      return source.url.substring(0, 40)
    }
  }
  if (isBlockSource(source)) {
    const title = source.blockTitle || source.blockId || $t('shared.citations.synthesis.defaultTitle')
    return $t('shared.citations.synthesis.labelWithTitle', { title })
  }
  return $t('shared.citations.sources.unknownSource')
}

const opensWebLink = (agg: AggregatedSource): boolean => isUrlSource(agg.source) && isWebUrl(agg.source.url)

const hasTraceAction = (agg: AggregatedSource): boolean => {
  // Block sources are always traceable - even without upstream sources,
  // we can show the block info in the panel
  return isBlockSource(agg.source)
}

const getCombinedCitedText = (agg: AggregatedSource): string | null => {
  if (agg.citedTexts.length === 0) return null
  if (agg.citedTexts.length === 1) return agg.citedTexts[0] ?? null
  return agg.citedTexts.join('\n\n...\n\n')
}

const getTruncatedCitedText = (agg: AggregatedSource, maxLength: number = 150): string | null => {
  const combined = getCombinedCitedText(agg)
  if (!combined) return null
  if (combined.length <= maxLength) return combined
  return combined.substring(0, maxLength) + '...'
}

// Event listener for SOURCE_CHAIN_EVENT
const handleSourceChainEvent = (event: Event) => {
  const { detail } = event as CustomEvent<SourceChainEventDetail>
  void openPanel(detail.blockSource, detail.citedSourcesByBlock ?? [], detail.opener, detail.fallback)
}

const handlePagePreviewEvent = (event: Event) => {
  const { detail } = event as CustomEvent<PagePreviewEventDetail>
  openPreview(detail.pageId, detail.title, detail.opener, detail.fallback ?? null)
}

// Handle escape key
const handleKeyDown = (event: KeyboardEvent) => {
  if (event.key === 'Escape' && !preview.value.open && isVisible.value) {
    closePanel()
  }
}

// Lifecycle
onMounted(() => {
  window.addEventListener(SOURCE_CHAIN_EVENT, handleSourceChainEvent)
  window.addEventListener(PAGE_PREVIEW_EVENT, handlePagePreviewEvent)
  document.addEventListener('keydown', handleKeyDown)
})

onUnmounted(() => {
  window.removeEventListener(SOURCE_CHAIN_EVENT, handleSourceChainEvent)
  window.removeEventListener(PAGE_PREVIEW_EVENT, handlePagePreviewEvent)
  document.removeEventListener('keydown', handleKeyDown)
})

// Reset cited text state when navigating between blocks
watch(currentBlock, () => {
  citedTextExpanded.value = false
  showAllDrawnFrom.value = false
  checkCitedTextOverflow()
})

// Recheck overflow when the template ref is assigned (covers v-if timing)
watch(citedTextRef, () => {
  checkCitedTextOverflow()
})

// Watch for external close
watch(() => props.isOpen, (newVal) => {
  if (!newVal && isVisible.value) {
    closePanel()
  }
})
</script>

<template>
  <!-- Backdrop -->
  <Teleport to="body">
    <Transition name="fade">
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions, vue/no-restricted-syntax -- backdrop only: Escape and the close button close the panel -->
      <div
        v-if="isVisible"
        class="source-chain-backdrop"
        @click="closePanel"
      />
    </Transition>
  </Teleport>

  <!-- Panel -->
  <Teleport to="body">
    <Transition name="slide">
      <FocusScope
        v-if="isVisible"
        ref="panelScope"
        as-child
        trapped
        loop
        @mount-auto-focus.prevent
        @unmount-auto-focus.prevent
      >
        <div
          class="source-chain-panel"
          role="dialog"
          aria-modal="true"
          aria-labelledby="source-chain-panel-title"
        >
          <!-- Header -->
          <div class="source-chain-header">
            <button
              type="button"
              class="source-chain-back-btn"
              @click="navigateBack"
              :title="$t('shared.citations.panel.goBack')"
              :aria-label="$t('shared.citations.panel.goBack')"
            >
              <svg aria-hidden="true" xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="m15 18-6-6 6-6"/>
              </svg>
            </button>
            <h3 id="source-chain-panel-title" class="source-chain-title">{{ $t('shared.citations.panel.title') }}</h3>
            <button
              type="button"
              class="source-chain-close-btn"
              @click="closePanel"
              :title="$t('shared.citations.panel.close')"
              :aria-label="$t('shared.citations.panel.close')"
            >
              <svg aria-hidden="true" xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M18 6 6 18"/>
                <path d="m6 6 12 12"/>
              </svg>
            </button>
          </div>

          <!-- Breadcrumbs -->
          <div v-if="breadcrumbs.length > 0" class="source-chain-breadcrumbs">
            <template v-for="(crumb, index) in breadcrumbs" :key="index">
              <button
                type="button"
                class="source-chain-breadcrumb"
                :class="{ active: index === breadcrumbs.length - 1 }"
                @click="navigateToBreadcrumb(index)"
              >
                {{ crumb }}
              </button>
              <span v-if="index < breadcrumbs.length - 1" class="source-chain-breadcrumb-separator">
                &rsaquo;
              </span>
            </template>
          </div>

          <!-- Current block info -->
          <div v-if="currentBlock?.citedText" class="source-chain-cited-text">
            <span class="source-chain-cited-label">{{ $t('shared.citations.panel.citedText') }}</span>
            <p
              ref="citedTextRef"
              class="source-chain-cited-content"
              :class="{ collapsed: !citedTextExpanded }"
            >"{{ currentBlock.citedText }}"</p>
            <button
              v-if="citedTextOverflows || citedTextExpanded"
              type="button"
              class="source-chain-cited-toggle"
              @click="citedTextExpanded = !citedTextExpanded"
            >
              {{ citedTextExpanded ? $t('shared.citations.panel.showLess') : $t('shared.citations.panel.showMore') }}
            </button>
          </div>

          <!-- Sources list -->
          <ScrollArea class="source-chain-content">
            <template v-for="section in sourceSections" :key="section.key">
              <div class="source-chain-sources-header">
                {{ section.title }} ({{ section.total }})
              </div>

              <div v-if="section.total === 0" class="source-chain-empty">
                <p>{{ $t('shared.citations.panel.emptyState') }}</p>
                <p class="source-chain-empty-hint">{{ $t('shared.citations.panel.emptyStateHint') }}</p>
              </div>

              <ul v-else class="source-chain-sources-list">
                <li
                  v-for="(agg, index) in section.items"
                  :key="index"
                  class="source-chain-source-item"
                  :class="{ 'is-match': agg.matchesFact }"
                >
                  <button
                    type="button"
                    class="source-chain-source-btn"
                    :aria-disabled="isUnverifiedDocumentSource(agg.source) || undefined"
                    @click="handleSourceClick(agg, $event)"
                    :class="{
                      clickable:
                        (isDocumentSource(agg.source) && !isUnverifiedDocumentSource(agg.source)) ||
                        opensWebLink(agg) ||
                        hasTraceAction(agg)
                    }"
                  >
                    <!-- Icon -->
                    <span class="source-chain-source-icon" :class="getSourceIcon(agg)">
                      <template v-if="isDocumentSource(agg.source)">
                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                          <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                          <polyline points="14,2 14,8 20,8"/>
                          <line x1="16" y1="13" x2="8" y2="13"/>
                          <line x1="16" y1="17" x2="8" y2="17"/>
                          <polyline points="10,9 9,9 8,9"/>
                        </svg>
                      </template>
                      <template v-else-if="isUrlSource(agg.source)">
                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                          <path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/>
                          <path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/>
                        </svg>
                      </template>
                      <template v-else-if="isBlockSource(agg.source)">
                        <!-- Gemini-style dual sparkle for synthesis sources -->
                        <svg width="16" height="14" viewBox="0 0 28 24" fill="currentColor">
                          <path d="M9 1C9 7 4 12 0 12C4 12 9 17 9 23C9 17 14 12 20 12C14 12 9 7 9 1Z"/>
                          <path d="M22 0C22 3 19.5 5 17 5C19.5 5 22 7 22 10C22 7 24.5 5 27 5C24.5 5 22 3 22 0Z" opacity="0.85"/>
                        </svg>
                      </template>
                    </span>

                    <!-- Label -->
                    <span class="source-chain-source-label">{{ getSourceLabel(agg) }}</span>
                    <span
                      v-if="isUnverifiedDocumentSource(agg.source)"
                      class="source-chain-unverified-badge"
                      :title="$t('shared.citations.unverified.tooltip')"
                    >{{ $t('shared.citations.unverified.badge') }}</span>

                    <!-- Trace arrow for blocks with upstream -->
                    <span v-if="hasTraceAction(agg)" class="source-chain-trace-arrow">
                      &rarr;
                    </span>

                    <!-- External link icon for URLs -->
                    <span v-else-if="opensWebLink(agg)" class="source-chain-external-icon">
                      <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>
                        <polyline points="15,3 21,3 21,9"/>
                        <line x1="10" y1="14" x2="21" y2="3"/>
                      </svg>
                    </span>
                  </button>

                  <!-- Cited text preview (combined if multiple) -->
                  <div v-if="getTruncatedCitedText(agg)" class="source-chain-source-cited">
                    "{{ getTruncatedCitedText(agg) }}"
                  </div>
                </li>
              </ul>

              <button
                v-if="section.items.length < section.total"
                type="button"
                class="fact-show-all-btn source-chain-show-all"
                @click="showAllDrawnFrom = true"
              >
                {{ $t('shared.citations.drawnFrom.showAll', { count: section.total }) }}
              </button>
            </template>
          </ScrollArea>
        </div>
      </FocusScope>
    </Transition>
  </Teleport>

  <PagePreviewDialog
    v-model:open="preview.open"
    :page-id="preview.pageId"
    :title="preview.title"
    :opener="preview.opener"
    :fallback-focus="preview.fallback"
  />
</template>

<style scoped>
/* Backdrop */
.source-chain-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.3);
  z-index: 998;
}

/* Panel */
.source-chain-panel {
  position: fixed;
  top: 0;
  right: 0;
  width: 360px;
  max-width: 90vw;
  height: 100vh;
  background: var(--bg-1, white);
  border-left: 1px solid var(--stroke, #e5e7eb);
  box-shadow: -4px 0 20px rgba(0, 0, 0, 0.1);
  z-index: 999;
  display: flex;
  flex-direction: column;
}

/* Header */
.source-chain-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 16px;
  border-bottom: 1px solid var(--stroke, #e5e7eb);
}

.source-chain-back-btn,
.source-chain-close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border: none;
  background: transparent;
  border-radius: 6px;
  cursor: pointer;
  color: var(--text-secondary, #6b7280);
  transition: background-color 0.15s, color 0.15s;
}

.source-chain-back-btn:hover,
.source-chain-close-btn:hover {
  background: var(--bg-2, #f3f4f6);
  color: var(--text-primary, #1f2937);
}

.source-chain-title {
  flex: 1;
  font-size: 16px;
  font-weight: 600;
  color: var(--text-primary, #1f2937);
  margin: 0;
}

/* Breadcrumbs */
.source-chain-breadcrumbs {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 4px;
  padding: 12px 16px;
  background: var(--bg-2, #f9fafb);
  border-bottom: 1px solid var(--stroke, #e5e7eb);
}

.source-chain-breadcrumb {
  background: none;
  border: none;
  padding: 4px 8px;
  border-radius: 4px;
  font-size: 13px;
  color: var(--text-secondary, #6b7280);
  cursor: pointer;
  max-width: 120px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  transition: background-color 0.15s, color 0.15s;
}

.source-chain-breadcrumb:hover {
  background: var(--bg-3);
  color: var(--text-primary, #1f2937);
}

.source-chain-breadcrumb.active {
  color: var(--accent-indigo-from, #667eea);
  font-weight: 500;
}

.source-chain-breadcrumb-separator {
  color: var(--text-tertiary, #9ca3af);
  font-size: 16px;
}

/* Cited text */
.source-chain-cited-text {
  padding: 12px 16px;
  background: var(--bg-2, #f9fafb);
  border-bottom: 1px solid var(--stroke, #e5e7eb);
}

.source-chain-cited-label {
  font-size: 11px;
  font-weight: 500;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-tertiary, #9ca3af);
}

.source-chain-cited-content {
  margin: 4px 0 0;
  font-size: 13px;
  font-style: italic;
  color: var(--text-secondary, #6b7280);
  line-height: 1.5;
  max-height: 30vh;
  overflow-y: auto;
}

.source-chain-cited-content.collapsed {
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
  max-height: none;
}

.source-chain-cited-toggle {
  background: none;
  border: none;
  padding: 4px 0 0;
  font-size: 12px;
  color: var(--accent-indigo-from, #667eea);
  cursor: pointer;
  font-weight: 500;
}

.source-chain-cited-toggle:hover {
  text-decoration: underline;
}

/* Content area */
.source-chain-content {
  flex: 1;
  overflow-y: auto;
}

.source-chain-sources-header {
  padding: 12px 16px;
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-tertiary, #9ca3af);
  border-bottom: 1px solid var(--stroke, #e5e7eb);
}

.source-chain-source-item.is-match .source-chain-source-label {
  font-weight: 600;
}

.source-chain-show-all {
  margin: 4px 16px 12px;
}

.source-chain-empty {
  padding: 24px 16px;
  text-align: center;
  color: var(--text-tertiary, #9ca3af);
  font-size: 14px;
}

.source-chain-empty p {
  margin: 0 0 8px;
}

.source-chain-empty-hint {
  font-size: 12px;
  color: var(--text-muted);
}

/* Sources list */
.source-chain-sources-list {
  list-style: none;
  margin: 0;
  padding: 0;
}

.source-chain-source-item {
  border-bottom: 1px solid var(--stroke, #e5e7eb);
}

.source-chain-source-btn {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 12px 16px;
  background: none;
  border: none;
  text-align: left;
  cursor: default;
  transition: background-color 0.15s;
}

.source-chain-source-btn.clickable {
  cursor: pointer;
}

.source-chain-source-btn.clickable:hover {
  background: var(--bg-2, #f9fafb);
}

.source-chain-source-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 6px;
  flex-shrink: 0;
}

.source-chain-source-icon.document {
  background: var(--accent-info-light);
  color: var(--accent-info);
}

.source-chain-source-icon.link {
  background: var(--accent-success-light);
  color: var(--accent-success);
}

.source-chain-source-icon.block {
  background: var(--secondary);
  color: var(--secondary-foreground);
}

.source-chain-source-label {
  flex: 1;
  font-size: 14px;
  color: var(--text-primary, #1f2937);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.source-chain-trace-arrow {
  color: var(--accent-indigo-from, #667eea);
  font-weight: 500;
}

.source-chain-external-icon {
  color: var(--text-tertiary, #9ca3af);
}

.source-chain-source-cited {
  padding: 0 16px 12px 54px;
  font-size: 12px;
  font-style: italic;
  color: var(--text-tertiary, #9ca3af);
  line-height: 1.4;
}

/* Transitions */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

.slide-enter-active,
.slide-leave-active {
  transition: transform 0.25s ease;
}

.slide-enter-from,
.slide-leave-to {
  transform: translateX(100%);
}
</style>
