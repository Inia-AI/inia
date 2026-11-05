<script setup lang="ts">
import useFinancialAnalysisTable from '@/tables/financialAnalysis'
import { DataTable } from '@/components/organisms'
import { IniaIcon } from '@/components/atoms'
import { Button } from '@/components/ui/button'
import { useRoute, useRouter } from 'vue-router'
import { reportRoute } from '@/lib/routes'
import API from '@/api'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ODataQueryParams } from '@/api/schema'
import type { Row, SortingState } from '@tanstack/vue-table'
import type { WorkflowReportSummary } from '@/api/workflowReports/schema'
import { buildReportsFilter } from '@/lib/reportsQuery'
import { IniaSelect, MultiSelect } from '@/components/molecules'
import { IniaLoader } from '@/components/atoms'
import { useWorkflowTypeNames } from '@/composables/useWorkflowTypeNames'
import type { ReportsListFilter, UseReportsListExtension } from '@/modules/extensionPoints'
import { useCanSeeTeam, useCapabilities } from '@/composables/useCapabilities'
import { CAPABILITIES } from '@/constants/capabilities'

const { useExtensions } = defineProps<{ useExtensions: UseReportsListExtension[] }>()

const router = useRouter()
const route = useRoute()
const { t: $t } = useI18n()

const parseQueryIds = (raw: unknown): string[] => {
  if (Array.isArray(raw)) {
    return raw.flatMap(v => (typeof v === 'string' ? v.split(',') : [])).filter(Boolean)
  }
  if (typeof raw === 'string' && raw) {
    return raw.split(',').filter(Boolean)
  }
  return []
}

const extensions = useExtensions.map(useExtension => useExtension())
const extensionFilters = extensions.flatMap(extension => extension.filters)
const extensionOverlays = extensions.flatMap(extension => (extension.overlay ? [extension.overlay] : []))

const { columns, rowLinkColumn } = useFinancialAnalysisTable(extensions.flatMap(extension => extension.columns))

const extensionFilterValue = (filter: ReportsListFilter, raw: unknown) => {
  const value = Array.isArray(raw) ? raw[0] : raw
  return filter.options.some(option => option.value === value) ? (value as string) : ''
}

const readExtensionFilters = (query: Record<string, unknown>) =>
  Object.fromEntries(
    extensionFilters.map(filter => [filter.queryKey, extensionFilterValue(filter, query[filter.queryKey])]),
  )

const filterOptions = computed(() =>
  Object.fromEntries(
    extensionFilters.map(filter => [
      filter.queryKey,
      [
        { label: $t(filter.allLabelKey), value: '' },
        ...filter.options.map(option => ({ label: $t(option.labelKey), value: option.value })),
      ],
    ]),
  ),
)

const page = ref(0)
const pageSize = ref(10)
const sorting = ref<SortingState>([])
const orderBy = ref('createdAt desc')
const workflowFilter = ref<string[]>(parseQueryIds(route.query.type))
const counterpartyFilter = ref<string[]>(parseQueryIds(route.query.cp))
const userFilter = ref<string[]>(parseQueryIds(route.query.user))
const extensionFilterValues = ref<Record<string, string>>(readExtensionFilters(route.query))
const reportsFilter = computed(() =>
  buildReportsFilter(
    {
      workflowTypeIds: workflowFilter.value,
      counterpartyIds: counterpartyFilter.value,
      userIds: userFilter.value,
    },
    extensionFilters.flatMap(filter => {
      const value = extensionFilterValues.value[filter.queryKey]
      return value ? [filter.toODataFilter(value)] : []
    }),
  ),
)
const hasActiveFilters = computed(() => reportsFilter.value !== undefined)

const clearFilters = () => {
  workflowFilter.value = []
  counterpartyFilter.value = []
  userFilter.value = []
  extensionFilterValues.value = readExtensionFilters({})
}

const counterpartyParams = ref({
  $top: 10000,
  $orderby: 'name asc',
  $select: 'id,name',
})
const usersParams = ref({
  $top: 10000,
})

const sameIds = (a: string[], b: string[]) =>
  a.length === b.length && a.every((v, i) => v === b[i])

watch(
  [workflowFilter, counterpartyFilter, userFilter, extensionFilterValues],
  ([type, cp, user, extensionValues]) => {
    const next: Record<string, string | undefined> = {
      ...(route.query as Record<string, string | undefined>),
      type: type.length ? type.join(',') : undefined,
      cp: cp.length ? cp.join(',') : undefined,
      user: user.length ? user.join(',') : undefined,
    }
    for (const [key, value] of Object.entries(extensionValues)) {
      next[key] = value || undefined
    }
    void router.replace({ query: Object.fromEntries(Object.entries(next).filter(([, v]) => v !== undefined)) })
    page.value = 0
  },
  { deep: true },
)

// Reverse sync: external URL changes (back/forward, manual edit, programmatic
// router.replace from elsewhere) update the filter refs. Guarded with sameIds
// so it doesn't echo back into the watcher above.
watch(
  () =>
    [
      route.query.type,
      route.query.cp,
      route.query.user,
      ...extensionFilters.map(filter => route.query[filter.queryKey]),
    ] as const,
  ([type, cp, user]) => {
    const nextType = parseQueryIds(type)
    const nextCp = parseQueryIds(cp)
    const nextUser = parseQueryIds(user)
    const nextExtensionValues = readExtensionFilters(route.query)
    if (!sameIds(workflowFilter.value, nextType)) {
      workflowFilter.value = nextType
    }
    if (!sameIds(counterpartyFilter.value, nextCp)) {
      counterpartyFilter.value = nextCp
    }
    if (!sameIds(userFilter.value, nextUser)) {
      userFilter.value = nextUser
    }
    if (extensionFilters.some(filter => extensionFilterValues.value[filter.queryKey] !== nextExtensionValues[filter.queryKey])) {
      extensionFilterValues.value = nextExtensionValues
    }
  },
)

const { workflowTypeOptions, withTypeName } = useWorkflowTypeNames()
const { data: counterparties } =
  API.Counterparties.useGetCounterparties(counterpartyParams)
const has = useCapabilities()
const canSeeTeam = useCanSeeTeam()
const { data: users } = API.Users.useGetUsers(usersParams, canSeeTeam)

const queryParams = computed<ODataQueryParams>(() => {
  const params: ODataQueryParams = {
    $skip: page.value * pageSize.value,
    $top: pageSize.value,
    $count: true,
  }

  if (sorting.value.length > 0) {
    const sort = sorting.value[0]
    if (sort) {
    params.$orderby = `${sort.id} ${sort.desc ? 'desc' : 'asc'}`
    }
  } else {
    params.$orderby = orderBy.value
  }

  if (reportsFilter.value) {
    params.$filter = reportsFilter.value
  }

  return params
})

const workflowOptions = computed(() =>
  workflowTypeOptions?.value?.map(x => ({
    label: x.workflowTypeName,
    value: x.workflowTypeId,
  })) ?? [],
)

const counterpartyOptions = computed(() =>
  counterparties?.value?.value?.map(x => ({
    label: x.name,
    value: x.id,
  })) ?? [],
)

const userOptions = computed(() =>
  users?.value?.value?.map(x => {
    const name = x.fullName?.trim()
    return {
      label: name ? `${name} (${x.email})` : x.email,
      value: x.id,
      searchText: [name, x.email].filter(Boolean).join(' '),
    }
  }) ?? [],
)

// Refetch the list every 5s so rows that complete while the table is open
// pick up the terminal state — and the live-ticking duration column flips
// from `now - startedAt` to the final server `duration`.
const { data, isLoading } =
  API.WorkflowReports.useGetWorkflowReports(queryParams, {
    refetchInterval: 5_000,
  })

// Get unique usernames (analystName) from workflow reports for resolution
const uniqueAnalystUsernames = computed(() => {
  if (!data?.value?.value) {
    return []
  }

  const usernames = data.value.value
    .map(wf => wf.analystName)
    .filter((name): name is string => !!name)

  return [...new Set(usernames)]
})

// Use the resolve endpoint which returns full names for usernames
const { data: resolvedUsers } = API.Users.useResolveUsers(uniqueAnalystUsernames)

const wfOutputs = computed(() =>
  (data.value?.value ?? []).map(withTypeName).map(x => ({
    ...x,
    analystName: resolvedUsers.value?.find(u => u.username === x.analystName)?.fullName || x.analystName,
  })),
)

const totalCount = computed(() => data.value?.count || 0)

const onCreate = () => {
  router.push({ name: 'CreateWorkflow' })
}

// Every row navigates — including Failed runs. The detail page is the
// only place to inspect what went wrong (logs, partial output, error
// message), so blocking the click would just send users hunting.
const onPageChange = (newPage: number) => {
  page.value = newPage
}

const onOrderByChange = (newOrderBy: string) => {
  orderBy.value = newOrderBy
  page.value = 0 // Reset to first page when sorting changes
}
</script>

<template>
  <div class="page-view">
    <div class="page-header-with-action">
      <h1 class="page-title">
        {{ $t(`page.financialAnalysis.title`) }}
      </h1>
      <Button v-if="has(CAPABILITIES.RUN)" class="btn-gradient-primary" @click="onCreate">
        <IniaIcon name="plus" class="mr-2" />
        {{ $t(`page.financialAnalysis.createWorkflowLabel`) }}
      </Button>
    </div>

    <div class="filters-section">
      <div class="filter-field">
        <span class="filter-label">{{
          $t('page.financialAnalysis.filters.reportType')
        }}</span>
        <MultiSelect
          v-model="workflowFilter"
          :options="workflowOptions"
          :aria-label="$t('page.financialAnalysis.filters.reportType')"
          :placeholder="$t('page.financialAnalysis.filters.all')"
          :max-badges="1"
        />
      </div>
      <div class="filter-field">
        <span class="filter-label">{{
          $t('page.financialAnalysis.filters.counterparty')
        }}</span>
        <MultiSelect
          v-model="counterpartyFilter"
          :options="counterpartyOptions"
          :aria-label="$t('page.financialAnalysis.filters.counterparty')"
          :placeholder="$t('page.financialAnalysis.filters.all')"
          :max-badges="1"
        />
      </div>
      <div v-if="canSeeTeam" class="filter-field">
        <span class="filter-label">{{
          $t('page.financialAnalysis.filters.user')
        }}</span>
        <MultiSelect
          v-model="userFilter"
          :options="userOptions"
          :aria-label="$t('page.financialAnalysis.filters.user')"
          :placeholder="$t('page.financialAnalysis.filters.all')"
          :max-badges="1"
        />
      </div>
      <div v-for="filter in extensionFilters" :key="filter.queryKey" class="filter-field">
        <span class="filter-label">{{ $t(filter.labelKey) }}</span>
        <IniaSelect
          v-model="extensionFilterValues[filter.queryKey]"
          :options="filterOptions[filter.queryKey]"
          :aria-label="$t(filter.labelKey)"
          :placeholder="$t(filter.allLabelKey)"
        />
      </div>
    </div>

    <div class="card-panel table-container">
      <DataTable
        v-if="wfOutputs && wfOutputs.length"
        :columns="columns"
        :data="wfOutputs"
        :page-size="pageSize"
        :total-count="totalCount"
        :page="page"
        :order-by="orderBy"
        :row-to="(row: Row<WorkflowReportSummary>) => reportRoute(row.original.id)"
        :row-link-column="rowLinkColumn"
        @update:page="onPageChange"
        @update:order-by="onOrderByChange"
      />
      <div v-else-if="!isLoading" class="empty-state" data-testid="reports-empty">
        <template v-if="hasActiveFilters">
          <p>{{ $t('page.financialAnalysis.emptyFilteredText') }}</p>
          <Button
            variant="outline"
            size="sm"
            class="mt-2"
            data-testid="reports-clear-filters"
            @click="clearFilters"
          >
            {{ $t('page.financialAnalysis.clearFilters') }}
          </Button>
        </template>
        <template v-else>
          {{ $t('page.financialAnalysis.emptyFinancialAnalysesText') }}
        </template>
      </div>
      <div v-if="isLoading" class="loading-overlay">
        <IniaLoader class="w-8" />
      </div>
    </div>

    <component :is="overlay" v-for="(overlay, index) in extensionOverlays" :key="index" />
  </div>
</template>

<style scoped>
/* ========================================
   FINANCIAL ANALYSIS - COMPONENT-SPECIFIC STYLES
   Only unique styles for this view
   ======================================== */

/* Filters section with multiple selects */
.filters-section {
  display: flex;
  gap: var(--spacing-sm);
  margin-bottom: var(--spacing-lg);
  align-items: flex-end;
}

.filter-field {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  flex: 0 0 220px;
}

.filter-field :deep(button) {
  width: 100%;
}

.filter-label {
  font-size: 0.75rem;
  font-weight: 500;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

/* Responsive: wrap filters on mobile */
@media (max-width: 768px) {
  .filters-section {
    flex-wrap: wrap;
  }

  .filter-field {
    flex: 1 1 150px;
  }
}
</style>
