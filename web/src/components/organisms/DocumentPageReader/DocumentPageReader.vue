<script setup lang="ts">
import { computed, nextTick, onMounted, ref, toRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import API from '@/api'
import type { DocumentPagesQueryParams, PageSummary } from '@/api/documents/schema'
import { IniaIcon } from '@/components/atoms'
import { Button } from '@/components/ui/button'
import { useDebouncedRef } from '@/composables/useDebounce'
import DocumentPageView from './DocumentPageView.vue'
import { conversionIcon, conversionLabelKey, conversionTone, locationLabelKey } from './pageConversion'

const LIST_SIZE = 50

const props = defineProps<{
  documentId: string
  pageCount: number
}>()

const pageNumber = defineModel<number>('pageNumber', { required: true })

const { t: $t } = useI18n()

const search = ref('')
const term = useDebouncedRef(search, 300)
const searchTerm = computed(() => term.value.trim())

const listWindowOf = (number: number) => Math.floor((number - 1) / LIST_SIZE)
const listPage = ref(listWindowOf(pageNumber.value))

watch(searchTerm, () => {
  listPage.value = searchTerm.value ? 0 : listWindowOf(pageNumber.value)
})

watch(pageNumber, number => {
  if (!searchTerm.value) {
    listPage.value = listWindowOf(number)
  }
})

const params = computed<DocumentPagesQueryParams>(() => ({
  $skip: listPage.value * LIST_SIZE,
  $top: LIST_SIZE,
  $count: true,
  $orderby: 'pageNumber asc',
  ...(searchTerm.value ? { term: searchTerm.value } : {}),
}))

const { data } = API.Documents.useGetDocumentPages(toRef(props, 'documentId'), params)

const pages = computed<PageSummary[]>(() => data.value?.value ?? [])
const total = computed(() => data.value?.count ?? 0)
const lastListPage = computed(() => Math.max(0, Math.ceil(total.value / LIST_SIZE) - 1))
const range = computed(() => ({
  from: listPage.value * LIST_SIZE + 1,
  to: Math.min((listPage.value + 1) * LIST_SIZE, total.value),
  total: total.value,
}))

const listElement = ref<HTMLElement | null>(null)

const showSelectedPage = () => {
  listElement.value?.querySelector<HTMLElement>('[aria-current="page"]')?.scrollIntoView({ block: 'nearest' })
}

onMounted(showSelectedPage)
watch([pageNumber, pages], () => nextTick(showSelectedPage))

const locationLabel = (location: string) => {
  const key = locationLabelKey(location)
  return key ? $t(key) : location
}
</script>

<template>
  <div class="page-reader">
    <nav class="page-list" :aria-label="$t('page.documents.detail.pages.title', { count: pageCount })">
      <h2 class="page-list-title">{{ $t('page.documents.detail.pages.title', { count: pageCount }) }}</h2>
      <input
        v-model="search"
        type="search"
        class="page-search"
        data-testid="page-search"
        :placeholder="$t('page.documents.detail.pages.searchPlaceholder')"
        :aria-label="$t('page.documents.detail.pages.searchPlaceholder')"
      />
      <ol v-if="pages.length" ref="listElement" class="page-list-items">
        <li v-for="page in pages" :key="page.id">
          <button
            type="button"
            :class="['page-list-item', { 'page-list-item--selected': page.pageNumber === pageNumber }]"
            :aria-current="page.pageNumber === pageNumber ? 'page' : undefined"
            data-testid="page-list-item"
            @click="pageNumber = page.pageNumber"
          >
            <span class="page-list-number">{{ page.pageNumber }}</span>
            <span class="page-list-location">{{ locationLabel(page.location) }}</span>
            <span
              :class="['page-list-state', `page-list-state--${conversionTone(page.status)}`]"
              :title="$t(conversionLabelKey(page.status))"
            >
              <IniaIcon :name="conversionIcon(page.status)" classes="w-3 h-3" />
              <span class="sr-only">{{ $t(conversionLabelKey(page.status)) }}</span>
            </span>
          </button>
        </li>
      </ol>
      <p v-else class="page-list-empty">
        {{
          searchTerm
            ? $t('page.documents.detail.pages.noMatch', { term: searchTerm })
            : $t('page.documents.detail.pages.empty')
        }}
      </p>
      <div v-if="total > LIST_SIZE" class="page-list-pager">
        <Button
          variant="ghost"
          size="icon"
          :aria-label="$t('page.documents.detail.pages.earlier')"
          :disabled="listPage <= 0"
          @click="listPage--"
        >
          <IniaIcon name="chevron-right" classes="pager-icon pager-icon--back" />
        </Button>
        <span class="page-list-range">{{ $t('page.documents.detail.pages.range', range) }}</span>
        <Button
          variant="ghost"
          size="icon"
          :aria-label="$t('page.documents.detail.pages.later')"
          :disabled="listPage >= lastListPage"
          @click="listPage++"
        >
          <IniaIcon name="chevron-right" classes="pager-icon" />
        </Button>
      </div>
    </nav>

    <DocumentPageView v-model:page-number="pageNumber" :document-id="documentId" :page-count="pageCount" />
  </div>
</template>

<style scoped>
.page-reader {
  display: grid;
  grid-template-columns: 15rem minmax(0, 1fr);
  gap: var(--spacing-lg);
  align-items: start;
}

.page-list {
  position: sticky;
  top: var(--spacing-md);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
  min-width: 0;
}

.page-list-title {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--text-primary);
}

.page-search {
  width: 100%;
  padding: 0.375rem 0.625rem;
  font-size: 0.8125rem;
  color: var(--text-primary);
  background: var(--bg-1);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-button);
}

.page-list-items {
  display: flex;
  flex-direction: column;
  max-height: 70vh;
  overflow-y: auto;
  margin: 0;
  padding: 0;
  list-style: none;
  border: 1px solid var(--stroke);
  border-radius: var(--radius-button);
}

.page-list-item {
  display: grid;
  grid-template-columns: 2.5rem minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--spacing-xs);
  width: 100%;
  padding: 0.375rem 0.625rem;
  font-size: 0.8125rem;
  text-align: left;
  color: var(--text-primary);
  background: transparent;
  border-left: 3px solid transparent;
}

li + li .page-list-item {
  border-top: 1px solid var(--stroke);
}

.page-list-item:hover {
  background: var(--bg-2);
}

.page-list-item--selected {
  background: var(--bg-2);
  border-left-color: var(--accent-indigo-from);
  font-weight: 600;
}

.page-list-number {
  font-variant-numeric: tabular-nums;
}

.page-list-location {
  color: var(--text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.page-list-state--good {
  color: var(--accent-success);
}

.page-list-state--warning {
  color: var(--accent-warning);
}

.page-list-state--failed {
  color: var(--accent-error);
}

.page-list-state--pending {
  color: var(--text-tertiary);
}

.page-list-empty {
  font-size: 0.8125rem;
  color: var(--text-secondary);
}

.page-list-pager {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.pager-icon--back {
  transform: rotate(180deg);
}

@media (max-width: 900px) {
  .page-reader {
    grid-template-columns: minmax(0, 1fr);
  }

  .page-list {
    position: static;
  }

  .page-list-items {
    max-height: 14rem;
  }
}
</style>
