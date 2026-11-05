<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, toRef } from 'vue'
import { useI18n } from 'vue-i18n'
import API from '@/api'
import { IniaIcon, IniaLoader, MarkdownText } from '@/components/atoms'
import PagePreviewDialog from '@/components/organisms/PagePreviewDialog/PagePreviewDialog.vue'
import { Button } from '@/components/ui/button'
import { useToast } from '@/components/ui/toast/use-toast'
import { usePageImage } from '@/composables/usePageImage'
import { conversionIcon, conversionLabelKey, conversionTone, locationLabelKey } from './pageConversion'

type TextMode = 'stored' | 'formatted'

const props = defineProps<{
  documentId: string
  pageCount: number
}>()

const pageNumber = defineModel<number>('pageNumber', { required: true })

const { t: $t } = useI18n()
const { toast } = useToast()

const { data: page, isLoading, isError } = API.Documents.useGetDocumentPage(
  toRef(props, 'documentId'),
  computed(() => pageNumber.value),
)

const pageId = computed(() => page.value?.id ?? null)
const image = usePageImage(pageId, ref(true))

const textMode = ref<TextMode>('stored')
const enlarged = ref(false)
const enlargeButton = ref<HTMLElement | null>(null)

const readAsImage = computed(() => page.value?.status === 'OCRNeedsReview')
const ocrFailed = computed(() => page.value?.status === 'OCRFailed')
const hasText = computed(() => Boolean(page.value?.text))
const hasNoImage = computed(() => image.text.value !== null)

const placement = computed(() => {
  if (!page.value) return ''
  const locationKey = locationLabelKey(page.value.location)
  const location = locationKey ? $t(locationKey) : page.value.location
  return `${$t('page.documents.detail.reader.filePage', { number: page.value.filePageNumber })}, ${location}`
})

const goTo = (number: number) => {
  if (number >= 1 && number <= props.pageCount && number !== pageNumber.value) {
    pageNumber.value = number
  }
}

const isTyping = (target: EventTarget | null) =>
  target instanceof HTMLElement &&
  (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))

const onKeydown = (event: KeyboardEvent) => {
  if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return
  if (isTyping(event.target) || enlarged.value) return
  if (event.key === 'ArrowLeft') goTo(pageNumber.value - 1)
  if (event.key === 'ArrowRight') goTo(pageNumber.value + 1)
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))

const copy = async (value: string | null | undefined) => {
  if (!value) return
  try {
    await navigator.clipboard.writeText(value)
    toast({ class: 'text-sm', description: $t('page.documents.detail.reader.copied'), duration: 1500 })
  } catch {
    toast({
      class: 'text-sm',
      variant: 'destructive',
      description: $t('page.documents.detail.reader.copyFailed'),
      duration: 3000,
    })
  }
}
</script>

<template>
  <section class="page-view" :aria-label="$t('page.documents.detail.reader.pageOf', { number: pageNumber, count: pageCount })">
    <header class="page-view-header">
      <div class="page-view-title">
        <h2 class="page-view-heading" aria-live="polite" aria-atomic="true">
          {{ $t('page.documents.detail.reader.pageOf', { number: pageNumber, count: pageCount }) }}
        </h2>
        <p v-if="page" class="page-view-meta">
          <span>{{ placement }}</span>
          <span :class="['conversion', `conversion--${conversionTone(page.status)}`]">
            <IniaIcon :name="conversionIcon(page.status)" classes="conversion-icon" />
            {{ $t(conversionLabelKey(page.status)) }}
          </span>
        </p>
        <p v-if="page && hasNoImage" class="page-view-meta" data-testid="no-page-image">
          {{ $t('page.documents.detail.reader.noImage') }}
        </p>
      </div>
      <div class="page-view-steps">
        <Button
          variant="outline"
          size="icon"
          :aria-label="$t('page.documents.detail.reader.previous')"
          :aria-disabled="pageNumber <= 1"
          @click="goTo(pageNumber - 1)"
        >
          <IniaIcon name="chevron-right" classes="step-icon step-icon--back" />
        </Button>
        <Button
          variant="outline"
          size="icon"
          :aria-label="$t('page.documents.detail.reader.next')"
          :aria-disabled="pageNumber >= pageCount"
          @click="goTo(pageNumber + 1)"
        >
          <IniaIcon name="chevron-right" classes="step-icon" />
        </Button>
      </div>
    </header>

    <div v-if="isLoading && !page" class="page-view-status">
      <IniaLoader class="w-6" />
    </div>
    <p v-else-if="isError" class="page-view-status page-view-status--error">
      {{ $t('page.documents.detail.reader.loadError') }}
    </p>

    <template v-else-if="page">
      <div :class="['page-view-panels', { 'page-view-panels--text-only': hasNoImage }]">
        <section v-if="!hasNoImage" class="panel" :aria-label="$t('page.documents.detail.reader.original')">
          <h3 class="panel-title">{{ $t('page.documents.detail.reader.original') }}</h3>
          <div class="panel-body panel-body--original" data-testid="page-original">
            <IniaLoader v-if="image.loading.value" class="w-6" />
            <p v-else-if="image.error.value" class="panel-note panel-note--error">{{ image.error.value }}</p>
            <button
              v-else-if="image.imageUrl.value"
              ref="enlargeButton"
              type="button"
              class="original-button"
              :aria-label="$t('page.documents.detail.reader.enlarge', { number: pageNumber })"
              @click="enlarged = true"
            >
              <img :src="image.imageUrl.value" :alt="$t('page.documents.detail.reader.original')" class="original-image" />
            </button>
          </div>
        </section>

        <section class="panel" :aria-label="$t('page.documents.detail.reader.text')">
          <div class="panel-title-row">
            <h3 class="panel-title">{{ $t('page.documents.detail.reader.text') }}</h3>
            <div v-if="hasText && !readAsImage" class="panel-tools">
              <div class="mode-toggle" role="group" :aria-label="$t('page.documents.detail.reader.text')">
                <button
                  type="button"
                  :class="['mode-option', { 'mode-option--on': textMode === 'stored' }]"
                  :aria-pressed="textMode === 'stored'"
                  @click="textMode = 'stored'"
                >
                  {{ $t('page.documents.detail.reader.asStored') }}
                </button>
                <button
                  type="button"
                  :class="['mode-option', { 'mode-option--on': textMode === 'formatted' }]"
                  :aria-pressed="textMode === 'formatted'"
                  @click="textMode = 'formatted'"
                >
                  {{ $t('page.documents.detail.reader.formatted') }}
                </button>
              </div>
              <Button
                variant="outline"
                size="sm"
                data-testid="copy-text"
                :aria-label="$t('page.documents.detail.reader.copyText', { number: pageNumber })"
                @click="copy(page.text)"
              >
                <IniaIcon name="copy" classes="w-3 h-3" />
                {{ $t('page.documents.detail.reader.copy') }}
              </Button>
            </div>
          </div>
          <div class="panel-body" tabindex="0" role="group" :aria-label="$t('page.documents.detail.reader.text')">
            <template v-if="readAsImage">
              <p class="panel-note panel-note--warning">{{ $t('page.documents.detail.reader.needsReview') }}</p>
              <template v-if="hasText">
                <h4 class="unused-title">{{ $t('page.documents.detail.reader.unusedText') }}</h4>
                <pre class="stored-text stored-text--unused">{{ page.text }}</pre>
              </template>
            </template>
            <p v-else-if="ocrFailed" class="panel-note panel-note--error">{{ $t('page.documents.detail.reader.ocrFailed') }}</p>
            <p v-else-if="!hasText" class="panel-note">{{ $t('page.documents.detail.reader.noText') }}</p>
            <pre v-else-if="textMode === 'stored'" class="stored-text" data-testid="page-text">{{ page.text }}</pre>
            <MarkdownText v-else class="formatted-text" :content="page.text" data-testid="page-text-formatted" />
          </div>
        </section>
      </div>

      <dl class="page-facts">
        <div class="fact">
          <dt>{{ $t('page.documents.detail.reader.overview') }}</dt>
          <dd class="fact-overview">
            <MarkdownText v-if="page.overview" class="fact-overview-text" :content="page.overview" data-testid="page-overview" />
            <span v-else class="fact-missing">{{ $t('page.documents.detail.reader.noOverview') }}</span>
            <Button
              v-if="page.overview"
              variant="ghost"
              size="sm"
              :aria-label="$t('page.documents.detail.reader.copyOverview', { number: pageNumber })"
              @click="copy(page.overview)"
            >
              <IniaIcon name="copy" classes="w-3 h-3" />
            </Button>
          </dd>
        </div>
        <div v-if="page.sectionTitles" class="fact">
          <dt>{{ $t('page.documents.detail.reader.sections') }}</dt>
          <dd>{{ page.sectionTitles }}</dd>
        </div>
        <div class="fact">
          <dt>{{ $t('page.documents.detail.reader.embedding') }}</dt>
          <dd>
            <span v-if="page.embeddingModel" class="mono">
              {{
                $t('page.documents.detail.reader.embeddingValue', {
                  model: page.embeddingModel,
                  dimensions: page.embeddingDimensions,
                })
              }}
            </span>
            <span v-else class="fact-missing">{{ $t('page.documents.detail.reader.noEmbedding') }}</span>
          </dd>
        </div>
        <div v-if="page.sourceUrl" class="fact">
          <dt>{{ $t('page.documents.detail.reader.source') }}</dt>
          <dd>
            <a :href="page.sourceUrl" target="_blank" rel="noopener noreferrer" class="fact-link">{{ page.sourceUrl }}</a>
          </dd>
        </div>
      </dl>
    </template>

    <PagePreviewDialog
      v-model:open="enlarged"
      :page-id="pageId"
      :title="$t('page.documents.detail.reader.pageOf', { number: pageNumber, count: pageCount })"
      :opener="enlargeButton"
    />
  </section>
</template>

<style scoped>
.page-view {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
  min-width: 0;
}

.page-view-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--spacing-md);
}

.page-view-heading {
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-primary);
}

.page-view-meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm);
  font-size: 0.8125rem;
  color: var(--text-secondary);
}

.conversion {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
}

.conversion--good :deep(.conversion-icon) {
  color: var(--accent-success);
}

.conversion--warning :deep(.conversion-icon) {
  color: var(--accent-warning);
}

.conversion--failed :deep(.conversion-icon) {
  color: var(--accent-error);
}

.conversion--pending :deep(.conversion-icon) {
  color: var(--text-tertiary);
}

.page-view-steps {
  display: flex;
  gap: var(--spacing-xs);
  flex-shrink: 0;
}

.step-icon--back {
  transform: rotate(180deg);
}

.page-view-status {
  display: flex;
  justify-content: center;
  padding: var(--spacing-lg);
  color: var(--text-secondary);
}

.page-view-status--error {
  color: var(--accent-error);
}

.page-view-panels {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: var(--spacing-md);
}

.page-view-panels--text-only {
  grid-template-columns: minmax(0, 1fr);
}

.panel {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  min-width: 0;
}

.panel-title-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-sm);
  min-height: 2rem;
}

.panel-title {
  display: flex;
  align-items: center;
  min-height: 2rem;
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--text-secondary);
}

.panel-tools {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
}

.mode-toggle {
  display: inline-flex;
  border: 1px solid var(--stroke);
  border-radius: var(--radius-button);
  overflow: hidden;
}

.mode-option {
  padding: 0.25rem 0.625rem;
  font-size: 0.75rem;
  color: var(--text-secondary);
  background: transparent;
}

.mode-option + .mode-option {
  border-left: 1px solid var(--stroke);
}

.mode-option--on {
  color: var(--text-primary);
  background: var(--bg-2);
  font-weight: 600;
}

.panel-body {
  height: calc(100vh - 30rem);
  min-height: 20rem;
  overflow: auto;
  padding: var(--spacing-sm);
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-button);
}

.panel-body--original {
  display: flex;
  align-items: flex-start;
  justify-content: center;
}

.original-button {
  display: block;
  cursor: zoom-in;
}

.original-image {
  display: block;
  max-width: 100%;
  height: auto;
  border-radius: var(--radius-button);
}

.stored-text {
  margin: 0;
  font-family: var(--font-mono, ui-monospace, monospace);
  font-size: 0.8125rem;
  line-height: 1.55;
  white-space: pre-wrap;
  word-break: break-word;
  color: var(--text-primary);
}

.stored-text--unused {
  color: var(--text-tertiary);
}

.formatted-text {
  font-size: 0.875rem;
}

.formatted-text :deep(table) {
  border-collapse: collapse;
  margin: 0.5em 0;
  font-size: 0.8125rem;
}

.formatted-text :deep(th),
.formatted-text :deep(td) {
  border: 1px solid var(--stroke);
  padding: 0.25rem 0.5rem;
  text-align: left;
  vertical-align: top;
}

.panel-note {
  font-size: 0.875rem;
  color: var(--text-secondary);
}

.panel-note--warning {
  color: var(--accent-warning);
}

.panel-note--error {
  color: var(--accent-error);
}

.unused-title {
  margin: var(--spacing-md) 0 var(--spacing-xs);
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--text-tertiary);
}

.page-facts {
  display: grid;
  gap: var(--spacing-xs);
  margin: 0;
}

.fact {
  display: grid;
  grid-template-columns: 7rem minmax(0, 1fr);
  gap: var(--spacing-sm);
  font-size: 0.875rem;
}

.fact dt {
  color: var(--text-secondary);
  font-weight: 600;
}

.fact dd {
  margin: 0;
  color: var(--text-primary);
  word-break: break-word;
}

.fact-overview {
  display: flex;
  align-items: flex-start;
  gap: var(--spacing-xs);
}

.fact-overview-text {
  flex: 1;
  min-width: 0;
  font-size: 0.875rem;
}

.fact-missing {
  color: var(--text-tertiary);
}

.fact-link {
  color: var(--accent-indigo-from);
  text-decoration: underline;
}

@media (max-width: 1100px) {
  .page-view-panels {
    grid-template-columns: minmax(0, 1fr);
  }

  .panel-body {
    height: auto;
    max-height: 70vh;
  }
}
</style>
