<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { asCalendarDay, parseServerDate } from '@/lib/utils'
import api from '@/api'
import type {
  OrganizationUsageDashboard,
  MyUsageSummary,
  TokenUsageSummary,
  UsageByType,
  TokenUsage,
  PricingComponentType,
} from '@/api/tokenUsage'

import { Badge } from '@/components/ui/badge'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import IniaLoader from '@/components/atoms/IniaLoader/IniaLoader.vue'
import TrendLineChart from '@/components/organisms/Charts/TrendLineChart.vue'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { escapeODataValue } from '@/utils/odata'
import {
  formatNumber,
  formatCurrency,
  getUsageTypeColor,
} from '@/utils/tokenUsageFormatting'
import { useCapabilities } from '@/composables/useCapabilities'
import { CAPABILITIES } from '@/constants/capabilities'

const { t: $t } = useI18n()

// View mode: personal or organization
const viewMode = ref<'personal' | 'organization'>('personal')

// Data
const orgDashboard = ref<OrganizationUsageDashboard | null>(null)
const myUsage = ref<MyUsageSummary | null>(null)
const isLoading = ref(false)
const error = ref<string | null>(null)
const selectedPeriod = ref<'today' | 'thisWeek' | 'thisMonth' | 'allTime'>('thisMonth')

// Details list state
const detailsList = ref<TokenUsage[]>([])
const detailsLoading = ref(false)
const detailsTotalCount = ref(0)
const detailsPage = ref(1)
const detailsPageSize = ref(20)
const expandedRows = ref<Set<string>>(new Set())

// Filter state
const filterModel = ref('')
const filterUsageType = ref('')
const filterDateFrom = ref('')
const filterDateTo = ref('')

// Usage type options for filter (uses startswith for category matching)
const usageTypeOptions = [
  { value: '', label: 'All Types' },
  { value: 'Llm', label: 'LLM Operations' },
  { value: 'Embedding', label: 'Embedding' },
  { value: 'Reranking', label: 'Reranking' },
]

// Component type display names
const componentTypeLabels: Record<PricingComponentType, string> = {
  Input: 'Input Tokens',
  Output: 'Output Tokens',
  CacheRead: 'Cache Read',
  CacheWrite5m: 'Cache Write (5m)',
  CacheWrite1h: 'Cache Write (1h)',
  Embedding: 'Embedding',
  Reasoning: 'Reasoning',
  ImageInput: 'Image Input',
  VideoInput: 'Video Input',
  AudioInput: 'Audio Input',
}

// Fetch organization dashboard data
const has = useCapabilities()

async function loadOrgDashboard() {
  orgDashboard.value = await api.TokenUsage.service.getOrganizationDashboard()
}

// Fetch personal usage data
async function loadMyUsage() {
  if (!has(CAPABILITIES.USAGE_COST)) {
    return
  }
  myUsage.value = await api.TokenUsage.service.getMyUsageSummary()
}

// Load all data
async function loadData() {
  isLoading.value = true
  error.value = null

  try {
    await Promise.all([loadMyUsage(), loadOrgDashboard()])
  } catch (e: unknown) {
    console.error('Failed to load usage data:', e)
    error.value = (e instanceof Error && e.message) || 'Failed to load usage data'
  } finally {
    isLoading.value = false
  }
}

// Load on mount
loadData()

// Load details list with OData
async function loadDetailsList() {
  detailsLoading.value = true
  try {
    const skip = (detailsPage.value - 1) * detailsPageSize.value
    const filters: string[] = []

    if (filterModel.value) {
      filters.push(`contains(tolower(modelId), '${escapeODataValue(filterModel.value.toLowerCase())}')`)
    }
    if (filterUsageType.value) {
      // Use startswith for category filtering (e.g., 'Llm' matches LlmBlockExecution, LlmReconciliation, etc.)
      filters.push(`startswith(usageType, '${escapeODataValue(filterUsageType.value)}')`)
    }
    if (filterDateFrom.value) {
      filters.push(`createdAt ge ${filterDateFrom.value}T00:00:00Z`)
    }
    if (filterDateTo.value) {
      filters.push(`createdAt le ${filterDateTo.value}T23:59:59Z`)
    }

    const result = await api.TokenUsage.service.listTokenUsage({
      $top: detailsPageSize.value,
      $skip: skip,
      $orderby: 'createdAt desc',
      $count: true,
      $filter: filters.length > 0 ? filters.join(' and ') : undefined,
    })

    if (result) {
      detailsList.value = result.value
      detailsTotalCount.value = result.count
    } else {
      detailsList.value = []
      detailsTotalCount.value = 0
    }
  } catch (e: unknown) {
    console.error('Failed to load details list:', e)
  } finally {
    detailsLoading.value = false
  }
}

// Toggle row expansion
function toggleRowExpand(id: string) {
  if (expandedRows.value.has(id)) {
    expandedRows.value.delete(id)
  } else {
    expandedRows.value.add(id)
  }
  // Trigger reactivity
  expandedRows.value = new Set(expandedRows.value)
}

// Reset filters
function resetFilters() {
  filterModel.value = ''
  filterUsageType.value = ''
  filterDateFrom.value = ''
  filterDateTo.value = ''
  detailsPage.value = 1
  loadDetailsList()
}

// Apply filters
function applyFilters() {
  detailsPage.value = 1
  loadDetailsList()
}

// Pagination
function goToPage(page: number) {
  detailsPage.value = page
  loadDetailsList()
}

const totalPages = computed(() => Math.ceil(detailsTotalCount.value / detailsPageSize.value))

// Format date for display
const USAGE_DATE_FORMAT = new Intl.DateTimeFormat('en-US', {
  month: 'short',
  day: 'numeric',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
})

function formatDate(dateStr: string): string {
  return USAGE_DATE_FORMAT.format(parseServerDate(dateStr))
}

// Get component color by type
function getComponentColor(type: PricingComponentType): string {
  const colors: Record<PricingComponentType, string> = {
    Input: 'bg-blue-100 text-blue-800',
    Output: 'bg-green-100 text-green-800',
    CacheRead: 'bg-purple-100 text-purple-800',
    CacheWrite5m: 'bg-orange-100 text-orange-800',
    CacheWrite1h: 'bg-amber-100 text-amber-800',
    Embedding: 'bg-indigo-100 text-indigo-800',
    Reasoning: 'bg-pink-100 text-pink-800',
    ImageInput: 'bg-cyan-100 text-cyan-800',
    VideoInput: 'bg-teal-100 text-teal-800',
    AudioInput: 'bg-rose-100 text-rose-800',
  }
  return colors[type] || 'bg-gray-100 text-gray-800'
}

// Watch for tab changes to load details on first visit
const activeTab = ref('overview')
watch(activeTab, (newTab) => {
  if (newTab === 'details' && detailsList.value.length === 0) {
    loadDetailsList()
  }
})

const hasData = computed(() => {
  if (viewMode.value === 'personal') {
    return !!myUsage.value
  }
  return !!orgDashboard.value
})

// Get current period summary based on view mode
const currentPeriodSummary = computed<TokenUsageSummary | null>(() => {
  if (viewMode.value === 'personal' && myUsage.value) {
    return myUsage.value[selectedPeriod.value]
  }
  if (viewMode.value === 'organization' && orgDashboard.value) {
    return orgDashboard.value[selectedPeriod.value]
  }
  return null
})

// Get usage by type based on view mode
const usageByType = computed<UsageByType[]>(() => {
  if (viewMode.value === 'personal' && myUsage.value) {
    return myUsage.value.byUsageType || []
  }
  if (viewMode.value === 'organization' && orgDashboard.value) {
    return orgDashboard.value.byUsageType || []
  }
  return []
})

const periodLabels = {
  today: 'Today',
  thisWeek: 'This Week',
  thisMonth: 'This Month',
  allTime: 'All Time',
}

// Chart data for daily trend (org only)
const trendChartData = computed(() => {
  if (viewMode.value !== 'organization' || !orgDashboard.value?.dailyTrend?.length) return null

  const dates = orgDashboard.value.dailyTrend.map(d => asCalendarDay(d.date).getDate())
  const series = [
    {
      name: 'Total Tokens',
      data: orgDashboard.value.dailyTrend.map(d => d.totalTokens),
      color: '#3B82F6',
    },
    {
      name: 'LLM Tokens',
      data: orgDashboard.value.dailyTrend.map(d => d.llmTokens),
      color: '#10B981',
    },
    {
      name: 'Embedding Tokens',
      data: orgDashboard.value.dailyTrend.map(d => d.embeddingTokens),
      color: '#8B5CF6',
    },
  ]

  return { dates, series }
})

const costTrendData = computed(() => {
  if (viewMode.value !== 'organization' || !orgDashboard.value?.dailyTrend?.length) return null

  const dates = orgDashboard.value.dailyTrend.map(d => asCalendarDay(d.date).getDate())
  const series = [
    {
      name: 'Daily Cost',
      data: orgDashboard.value.dailyTrend.map(d => d.totalCost),
      color: '#F59E0B',
    },
  ]

  return { dates, series }
})

// Page title and subtitle based on view mode
const pageTitle = computed(() => {
  return viewMode.value === 'personal' ? 'My Token Usage' : 'Organization Token Usage'
})

const pageSubtitle = computed(() => {
  if (viewMode.value === 'personal') {
    return myUsage.value?.userDisplayName
      ? `Usage statistics for ${myUsage.value.userDisplayName}`
      : 'Your personal AI token consumption and costs'
  }
  return orgDashboard.value?.organizationName
    ? `Usage statistics for ${orgDashboard.value.organizationName}`
    : 'Organization-wide AI token consumption and costs'
})

// Helper functions
function getPercentageWidth(percentage: number): string {
  return `${Math.min(percentage, 100)}%`
}
</script>

<template>
  <div class="token-usage-analytics">
    <!-- Header -->
    <div class="page-header">
      <div class="header-content">
        <div>
          <h1 class="page-title">
            <IniaIcon name="chart-bar" class="text-primary" />
            {{ pageTitle }}
          </h1>
          <p class="page-subtitle">{{ pageSubtitle }}</p>
        </div>
        <button
          type="button"
          @click="loadData"
          :disabled="isLoading"
          class="refresh-button"
        >
          <IniaIcon
            name="arrows-rotate"
            class="text-sm"
            :class="{ 'animate-spin': isLoading }"
          />
          Refresh
        </button>
      </div>

      <!-- View Mode Selector -->
      <div class="view-mode-selector">
        <button
          type="button"
          @click="viewMode = 'personal'"
          :class="['view-mode-button', { active: viewMode === 'personal' }]"
        >
          <IniaIcon name="user" class="text-sm" />
          My Usage
        </button>
        <button
          type="button"
          @click="viewMode = 'organization'"
          :class="['view-mode-button', { active: viewMode === 'organization' }]"
        >
          <IniaIcon name="users" class="text-sm" />
          Organization
        </button>
      </div>

      <!-- Period Selector -->
      <div v-if="hasData" class="period-selector">
        <button
          v-for="(label, key) in periodLabels"
          type="button"
          :key="key"
          @click="selectedPeriod = key as typeof selectedPeriod"
          :class="['period-button', { active: selectedPeriod === key }]"
        >
          {{ label }}
        </button>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="isLoading && !hasData" class="loading-state">
      <IniaLoader />
      <p>Loading usage analytics...</p>
    </div>

    <!-- Error State -->
    <div v-else-if="error" class="error-state">
      <IniaIcon name="circle-exclamation" class="text-4xl text-error mb-4" />
      <p class="text-error mb-4">{{ error }}</p>
      <button type="button" @click="loadData" class="retry-button">
        Try Again
      </button>
    </div>

    <!-- Dashboard Content -->
    <div v-else-if="hasData" class="dashboard-content">
      <!-- Summary Cards -->
      <div class="summary-cards">
        <div class="summary-card">
          <div class="card-icon bg-[var(--accent-info-light)] text-info">
            <IniaIcon name="hashtag" />
          </div>
          <div class="card-content">
            <div class="card-value">{{ formatNumber(currentPeriodSummary?.totalTokens || 0) }}</div>
            <div class="card-label">Total Tokens</div>
            <div class="card-detail">
              {{ formatNumber(currentPeriodSummary?.totalInputTokens || 0) }} in /
              {{ formatNumber(currentPeriodSummary?.totalOutputTokens || 0) }} out
            </div>
          </div>
        </div>

        <div class="summary-card">
          <div class="card-icon bg-[var(--accent-success-light)] text-success">
            <IniaIcon name="dollar-sign" />
          </div>
          <div class="card-content">
            <div class="card-value">{{ formatCurrency(currentPeriodSummary?.totalCost || 0) }}</div>
            <div class="card-label">Total Cost</div>
            <div class="card-detail">{{ periodLabels[selectedPeriod] }}</div>
          </div>
        </div>

        <div class="summary-card">
          <div class="card-icon bg-secondary text-secondary-foreground">
            <IniaIcon name="play" />
          </div>
          <div class="card-content">
            <div class="card-value">{{ formatNumber(currentPeriodSummary?.operationCount || 0) }}</div>
            <div class="card-label">Operations</div>
            <div class="card-detail">AI requests made</div>
          </div>
        </div>

        <div class="summary-card">
          <div class="card-icon bg-[var(--accent-warning-light)] text-warning">
            <IniaIcon name="database" />
          </div>
          <div class="card-content">
            <div class="card-value">{{ formatNumber(currentPeriodSummary?.totalCacheReadTokens || 0) }}</div>
            <div class="card-label">Cache Hits</div>
            <div class="card-detail">Tokens saved from cache</div>
          </div>
        </div>
      </div>

      <!-- Tabs for different views -->
      <Tabs v-model="activeTab" class="analytics-tabs">
        <TabsList class="tabs-list">
          <TabsTrigger value="overview">Overview</TabsTrigger>
          <TabsTrigger v-if="viewMode === 'organization'" value="workflows">By Workflow</TabsTrigger>
          <TabsTrigger v-if="viewMode === 'organization'" value="users">By User</TabsTrigger>
          <TabsTrigger v-if="viewMode === 'organization'" value="trends">Trends</TabsTrigger>
          <TabsTrigger value="details">Details</TabsTrigger>
        </TabsList>

        <!-- Overview Tab -->
        <TabsContent value="overview" class="tab-content">
          <div class="grid-2-cols">
            <!-- Usage by Type -->
            <div class="panel">
              <h2 class="panel-title">Usage by Type</h2>
              <div class="panel-content">
                <div v-if="!usageByType.length" class="empty-state">
                  No usage data yet
                </div>
                <div v-else class="usage-list">
                  <div
                    v-for="usage in usageByType"
                    :key="usage.usageType"
                    class="usage-item"
                  >
                    <div class="usage-item-header">
                      <Badge :class="getUsageTypeColor(usage.usageType)">
                        {{ usage.displayName }}
                      </Badge>
                      <span class="usage-percentage">{{ usage.percentage.toFixed(1) }}%</span>
                    </div>
                    <div class="progress-bar">
                      <div
                        class="progress-fill bg-indigo-500"
                        :style="{ width: getPercentageWidth(usage.percentage) }"
                      />
                    </div>
                    <div class="usage-item-footer">
                      <span>{{ formatNumber(usage.totalTokens) }} tokens</span>
                      <span>{{ formatCurrency(usage.totalCost) }}</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <!-- Info Panel (different for personal vs org) -->
            <div class="panel">
              <h2 class="panel-title">{{ viewMode === 'personal' ? 'Account Info' : 'Organization Info' }}</h2>
              <div class="panel-content">
                <div class="org-info">
                  <div class="org-name">
                    {{ viewMode === 'personal'
                      ? (myUsage?.userDisplayName || 'Your Account')
                      : (orgDashboard?.organizationName || 'Organization')
                    }}
                  </div>
                  <div class="org-stats">
                    <template v-if="viewMode === 'organization' && orgDashboard">
                      <div class="stat-row">
                        <span class="stat-label">Active Users</span>
                        <span class="stat-value">{{ orgDashboard.byUser?.length || 0 }}</span>
                      </div>
                      <div class="stat-row">
                        <span class="stat-label">Active Workflows</span>
                        <span class="stat-value">{{ orgDashboard.byWorkflowType?.length || 0 }}</span>
                      </div>
                    </template>
                    <div class="stat-row">
                      <span class="stat-label">Cache Creation</span>
                      <span class="stat-value">{{ formatNumber(currentPeriodSummary?.totalCacheCreationTokens || 0) }} tokens</span>
                    </div>
                    <div class="stat-row">
                      <span class="stat-label">Input Tokens</span>
                      <span class="stat-value">{{ formatNumber(currentPeriodSummary?.totalInputTokens || 0) }}</span>
                    </div>
                    <div class="stat-row">
                      <span class="stat-label">Output Tokens</span>
                      <span class="stat-value">{{ formatNumber(currentPeriodSummary?.totalOutputTokens || 0) }}</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </TabsContent>

        <!-- By Workflow Tab (Org only) -->
        <TabsContent v-if="viewMode === 'organization'" value="workflows" class="tab-content">
          <div class="panel full-width">
            <h2 class="panel-title">Usage by Workflow Type</h2>
            <div class="panel-content">
              <div v-if="!orgDashboard?.byWorkflowType?.length" class="empty-state">
                No workflow usage data yet
              </div>
              <div v-else class="workflow-table">
                <div class="table-header">
                  <div class="col-workflow">Workflow</div>
                  <div class="col-executions">Executions</div>
                  <div class="col-tokens">Tokens</div>
                  <div class="col-cost">Cost</div>
                  <div class="col-percentage">Share</div>
                </div>
                <div
                  v-for="wf in orgDashboard.byWorkflowType"
                  :key="wf.workflowTypeId"
                  class="table-row"
                >
                  <div class="col-workflow">{{ wf.workflowTypeName }}</div>
                  <div class="col-executions">{{ wf.executionCount }}</div>
                  <div class="col-tokens">{{ formatNumber(wf.totalTokens) }}</div>
                  <div class="col-cost">{{ formatCurrency(wf.totalCost) }}</div>
                  <div class="col-percentage">
                    <div class="percentage-bar">
                      <div
                        class="percentage-fill"
                        :style="{ width: getPercentageWidth(wf.percentage) }"
                      />
                      <span>{{ wf.percentage.toFixed(1) }}%</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </TabsContent>

        <!-- By User Tab (Org only) -->
        <TabsContent v-if="viewMode === 'organization'" value="users" class="tab-content">
          <div class="panel full-width">
            <h2 class="panel-title">Usage by User</h2>
            <div class="panel-content">
              <div v-if="!orgDashboard?.byUser?.length" class="empty-state">
                No user usage data yet
              </div>
              <div v-else class="user-table">
                <div class="table-header">
                  <div class="col-user">User</div>
                  <div class="col-operations">Operations</div>
                  <div class="col-tokens">Tokens</div>
                  <div class="col-cost">Cost</div>
                  <div class="col-percentage">Share</div>
                </div>
                <div
                  v-for="user in orgDashboard.byUser"
                  :key="user.userId"
                  class="table-row"
                >
                  <div class="col-user">
                    <IniaIcon name="user" class="text-muted-foreground mr-2" />
                    {{ user.userDisplayName || 'Unknown User' }}
                  </div>
                  <div class="col-operations">{{ user.operationCount }}</div>
                  <div class="col-tokens">{{ formatNumber(user.totalTokens) }}</div>
                  <div class="col-cost">{{ formatCurrency(user.totalCost) }}</div>
                  <div class="col-percentage">
                    <div class="percentage-bar">
                      <div
                        class="percentage-fill"
                        :style="{ width: getPercentageWidth(user.percentage) }"
                      />
                      <span>{{ user.percentage.toFixed(1) }}%</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </TabsContent>

        <!-- Trends Tab (Org only) -->
        <TabsContent v-if="viewMode === 'organization'" value="trends" class="tab-content">
          <div class="grid-2-cols">
            <!-- Token Trend Chart -->
            <div class="panel">
              <h2 class="panel-title">Token Usage Trend (Last 30 Days)</h2>
              <div class="panel-content chart-container">
                <div v-if="!trendChartData" class="empty-state">
                  No trend data available
                </div>
                <TrendLineChart
                  v-else
                  :x-values="trendChartData.dates"
                  :series="trendChartData.series"
                  y-values=""
                  unit="tokens"
                  title=""
                  :meta="{ xLabel: 'Day of Month', yLabel: 'Tokens' }"
                  :display-options="{ showLegend: true, showGridLines: true, enableAnimations: true }"
                  :layout="{ height: '300px' }"
                />
              </div>
            </div>

            <!-- Cost Trend Chart -->
            <div class="panel">
              <h2 class="panel-title">Cost Trend (Last 30 Days)</h2>
              <div class="panel-content chart-container">
                <div v-if="!costTrendData" class="empty-state">
                  No cost trend data available
                </div>
                <TrendLineChart
                  v-else
                  :x-values="costTrendData.dates"
                  :series="costTrendData.series"
                  y-values="$"
                  unit=""
                  title=""
                  :meta="{ xLabel: 'Day of Month', yLabel: 'Cost (USD)' }"
                  :display-options="{ showLegend: false, showGridLines: true, enableAnimations: true }"
                  :layout="{ height: '300px' }"
                />
              </div>
            </div>
          </div>
        </TabsContent>

        <!-- Details Tab - Filterable List -->
        <TabsContent value="details" class="tab-content">
          <div class="panel full-width">
            <h2 class="panel-title">Usage Details</h2>
            <div class="panel-content">
              <!-- Filters -->
              <div class="details-filters">
                <div class="filter-row">
                  <div class="filter-group">
                    <label for="token-usage-model" class="filter-label">Model</label>
                    <Input
                      id="token-usage-model"
                      v-model="filterModel"
                      placeholder="Filter by model..."
                      class="filter-input"
                    />
                  </div>
                  <div class="filter-group">
                    <label for="token-usage-type" class="filter-label">Usage Type</label>
                    <select id="token-usage-type" v-model="filterUsageType" class="filter-select">
                      <option v-for="opt in usageTypeOptions" :key="opt.value" :value="opt.value">
                        {{ opt.label }}
                      </option>
                    </select>
                  </div>
                  <div class="filter-group">
                    <label for="token-usage-from" class="filter-label">From Date</label>
                    <Input
                      id="token-usage-from"
                      v-model="filterDateFrom"
                      type="date"
                      class="filter-input"
                    />
                  </div>
                  <div class="filter-group">
                    <label for="token-usage-to" class="filter-label">To Date</label>
                    <Input
                      id="token-usage-to"
                      v-model="filterDateTo"
                      type="date"
                      class="filter-input"
                    />
                  </div>
                  <div class="filter-actions">
                    <Button @click="applyFilters" class="apply-btn">
                      <IniaIcon name="magnifying-glass" class="mr-1" />
                      Apply
                    </Button>
                    <Button variant="outline" @click="resetFilters" class="reset-btn">
                      Reset
                    </Button>
                  </div>
                </div>
              </div>

              <!-- Loading -->
              <div v-if="detailsLoading" class="loading-state compact">
                <IniaLoader />
                <p>Loading usage details...</p>
              </div>

              <!-- Table -->
              <div v-else-if="detailsList.length > 0" class="details-table-container">
                <div class="details-table">
                  <div class="details-header">
                    <div class="col-expand"></div>
                    <div class="col-date">Date</div>
                    <div class="col-type">Type</div>
                    <div class="col-model">Model</div>
                    <div class="col-context">Context</div>
                    <div class="col-description">Description</div>
                    <div class="col-units">Total Units</div>
                    <div class="col-cost">Cost</div>
                  </div>
                  <template v-for="record in detailsList" :key="record.id">
                    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions, vue/no-restricted-syntax -- the expand button in the row is the keyboard path; its click bubbles here -->
                    <div class="details-row" @click="toggleRowExpand(record.id)">
                      <div class="col-expand">
                        <button
                          type="button"
                          class="expand-button focus-ring"
                          :aria-expanded="expandedRows.has(record.id)"
                          :aria-label="$t('page.tokenUsage.details.breakdownFor', { date: formatDate(record.createdAt) })"
                        >
                          <IniaIcon
                            :name="expandedRows.has(record.id) ? 'chevron-down' : 'chevron-right'"
                            class="expand-icon"
                          />
                        </button>
                      </div>
                      <div class="col-date">{{ formatDate(record.createdAt) }}</div>
                      <div class="col-type">
                        <Badge :class="getUsageTypeColor(record.usageType)">
                          {{ record.usageType }}
                        </Badge>
                      </div>
                      <div class="col-model">
                        <span class="model-provider">{{ record.provider }}</span>
                        <span class="model-id">{{ record.modelId }}</span>
                      </div>
                      <div class="col-context">
                        <span v-if="record.workflowTypeName" class="context-workflow">
                          {{ record.workflowTypeName }}
                        </span>
                        <span v-else-if="record.executionContext" class="context-text">
                          {{ record.executionContext }}
                        </span>
                        <span v-else class="context-text text-muted-foreground">-</span>
                      </div>
                      <div class="col-description">
                        <span v-if="record.description" class="description-text" :title="record.description">
                          {{ record.description }}
                        </span>
                        <span v-else class="text-muted-foreground">-</span>
                      </div>
                      <div class="col-units">
                        {{ formatNumber(record.components.reduce((sum, c) => sum + c.units, 0)) }}
                      </div>
                      <div class="col-cost">{{ formatCurrency(record.totalCost) }}</div>
                    </div>
                    <!-- Expanded Component Details -->
                    <div v-if="expandedRows.has(record.id)" class="details-expanded">
                      <div class="components-grid">
                        <div
                          v-for="component in record.components"
                          :key="component.componentType"
                          class="component-item"
                        >
                          <Badge :class="getComponentColor(component.componentType)">
                            {{ componentTypeLabels[component.componentType] || component.componentType }}
                          </Badge>
                          <div class="component-values">
                            <span class="component-units">{{ formatNumber(component.units) }} units</span>
                            <span class="component-cost">{{ formatCurrency(component.cost) }}</span>
                          </div>
                        </div>
                      </div>
                      <div v-if="record.userDisplayName" class="expanded-meta">
                        <IniaIcon name="user" class="text-muted-foreground" />
                        <span>{{ record.userDisplayName }}</span>
                      </div>
                    </div>
                  </template>
                </div>

                <!-- Pagination -->
                <div class="pagination-controls">
                  <span class="pagination-info">
                    Showing {{ (detailsPage - 1) * detailsPageSize + 1 }} -
                    {{ Math.min(detailsPage * detailsPageSize, detailsTotalCount) }}
                    of {{ detailsTotalCount }}
                  </span>
                  <div class="pagination-buttons">
                    <Button
                      variant="outline"
                      size="sm"
                      :disabled="detailsPage === 1"
                      @click="goToPage(detailsPage - 1)"
                    >
                      Previous
                    </Button>
                    <span class="page-indicator">Page {{ detailsPage }} of {{ totalPages }}</span>
                    <Button
                      variant="outline"
                      size="sm"
                      :disabled="detailsPage >= totalPages"
                      @click="goToPage(detailsPage + 1)"
                    >
                      Next
                    </Button>
                  </div>
                </div>
              </div>

              <!-- Empty State -->
              <div v-else class="empty-state">
                <IniaIcon name="file-circle-question" class="text-3xl text-muted-foreground mb-2" />
                <p>No usage records found</p>
                <p class="text-sm">Try adjusting your filters or start using AI features</p>
              </div>
            </div>
          </div>
        </TabsContent>
      </Tabs>
    </div>

    <!-- Empty State -->
    <div v-else class="empty-dashboard">
      <IniaIcon name="chart-bar" class="text-6xl text-muted-foreground mb-4" />
      <h2 class="text-lg font-medium mb-2">No Usage Data</h2>
      <p class="text-muted-foreground">
        Start using AI workflows to see usage analytics.
      </p>
    </div>
  </div>
</template>

<style scoped>
.token-usage-analytics {
  min-height: 100vh;
  background: var(--bg-0, #f8fafc);
  padding: var(--spacing-xl, 2rem);
}

.page-header {
  margin-bottom: var(--spacing-xl, 2rem);
}

.header-content {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: var(--spacing-md, 1rem);
}

.page-title {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  font-size: 1.75rem;
  font-weight: 700;
  color: var(--text-primary, #1f2937);
  margin-bottom: 0.25rem;
}

.page-subtitle {
  color: var(--text-secondary, #6b7280);
  font-size: 0.875rem;
}

.refresh-button {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
  background: var(--bg-1);
  border: 1px solid var(--stroke, #e5e7eb);
  border-radius: 0.5rem;
  font-size: 0.875rem;
  color: var(--text-secondary, #6b7280);
  cursor: pointer;
  transition: all 0.15s;
}

.refresh-button:hover:not(:disabled) {
  background: var(--bg-1, #f3f4f6);
  color: var(--text-primary, #1f2937);
}

.refresh-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.view-mode-selector {
  display: flex;
  gap: 0.5rem;
  margin-bottom: var(--spacing-md, 1rem);
}

.view-mode-button {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.625rem 1.25rem;
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--text-secondary, #6b7280);
  background: var(--bg-1);
  border: 1px solid var(--stroke, #e5e7eb);
  border-radius: 0.5rem;
  cursor: pointer;
  transition: all 0.15s;
}

.view-mode-button:hover:not(.active) {
  background: var(--bg-1, #f3f4f6);
  border-color: var(--text-muted, #9ca3af);
}

.view-mode-button.active {
  background: var(--primary);
  color: var(--primary-foreground);
  border-color: transparent;
  box-shadow: var(--shadow-brand);
}

.period-selector {
  display: flex;
  gap: 0.25rem;
  background: var(--bg-1);
  border: 1px solid var(--stroke, #e5e7eb);
  border-radius: 0.5rem;
  padding: 0.25rem;
  width: fit-content;
}

.period-button {
  padding: 0.5rem 1rem;
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--text-secondary, #6b7280);
  background: transparent;
  border: none;
  border-radius: 0.375rem;
  cursor: pointer;
  transition: all 0.15s;
}

.period-button:hover:not(.active) {
  background: var(--bg-1, #f3f4f6);
}

.period-button.active {
  background: var(--primary);
  color: var(--primary-foreground);
}

.loading-state,
.error-state,
.empty-dashboard {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 400px;
  text-align: center;
  color: var(--text-secondary, #6b7280);
}

.retry-button {
  padding: 0.5rem 1rem;
  background: var(--accent-error-light);
  color: var(--accent-error);
  border: none;
  border-radius: 0.5rem;
  cursor: pointer;
}

.retry-button:hover {
  background: var(--accent-error-light);
}

.summary-cards {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: var(--spacing-md, 1rem);
  margin-bottom: var(--spacing-xl, 2rem);
}

@media (max-width: 1024px) {
  .summary-cards {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 640px) {
  .summary-cards {
    grid-template-columns: 1fr;
  }
}

.summary-card {
  display: flex;
  align-items: flex-start;
  gap: 1rem;
  padding: 1.25rem;
  background: var(--bg-1);
  border: 1px solid var(--stroke, #e5e7eb);
  border-radius: 0.75rem;
}

.card-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  border-radius: 0.5rem;
  font-size: 1.25rem;
}

.card-content {
  flex: 1;
}

.card-value {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--text-primary, #1f2937);
}

.card-label {
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--text-secondary, #6b7280);
}

.card-detail {
  font-size: 0.75rem;
  color: var(--text-muted, #9ca3af);
  margin-top: 0.25rem;
}

.analytics-tabs {
  margin-top: var(--spacing-md, 1rem);
}

.tabs-list {
  background: var(--bg-1);
  border: 1px solid var(--stroke, #e5e7eb);
  border-radius: 0.5rem;
  padding: 0.25rem;
  width: fit-content;
}

.tab-content {
  margin-top: var(--spacing-md, 1rem);
}

.grid-2-cols {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: var(--spacing-md, 1rem);
}

@media (max-width: 768px) {
  .grid-2-cols {
    grid-template-columns: 1fr;
  }
}

.panel {
  background: var(--bg-1);
  border: 1px solid var(--stroke, #e5e7eb);
  border-radius: 0.75rem;
  overflow: hidden;
}

.panel.full-width {
  grid-column: 1 / -1;
}

.panel-title {
  padding: 1rem 1.25rem;
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-primary, #1f2937);
  border-bottom: 1px solid var(--stroke, #e5e7eb);
}

.panel-content {
  padding: 1.25rem;
}

.empty-state {
  text-align: center;
  color: var(--text-muted, #9ca3af);
  padding: 2rem;
}

.usage-list {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.usage-item {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.usage-item-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.usage-percentage {
  font-size: 0.875rem;
  font-weight: 500;
  color: var(--text-secondary, #6b7280);
}

.progress-bar {
  height: 6px;
  background: var(--bg-1, #f3f4f6);
  border-radius: 3px;
  overflow: hidden;
}

.progress-fill {
  height: 100%;
  border-radius: 3px;
  transition: width 0.3s ease;
}

.usage-item-footer {
  display: flex;
  justify-content: space-between;
  font-size: 0.75rem;
  color: var(--text-muted, #9ca3af);
}

.org-info {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.org-name {
  font-size: 1.25rem;
  font-weight: 600;
  color: var(--text-primary, #1f2937);
}

.org-stats {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.stat-row {
  display: flex;
  justify-content: space-between;
  padding: 0.5rem 0;
  border-bottom: 1px solid var(--stroke, #e5e7eb);
}

.stat-row:last-child {
  border-bottom: none;
}

.stat-label {
  color: var(--text-secondary, #6b7280);
  font-size: 0.875rem;
}

.stat-value {
  font-weight: 500;
  color: var(--text-primary, #1f2937);
}

.workflow-table,
.user-table {
  width: 100%;
}

.table-header {
  display: grid;
  grid-template-columns: 2fr 1fr 1fr 1fr 1.5fr;
  gap: 1rem;
  padding: 0.75rem 1rem;
  background: var(--bg-1, #f3f4f6);
  border-radius: 0.5rem;
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--text-secondary, #6b7280);
  text-transform: uppercase;
}

.table-row {
  display: grid;
  grid-template-columns: 2fr 1fr 1fr 1fr 1.5fr;
  gap: 1rem;
  padding: 1rem;
  border-bottom: 1px solid var(--stroke, #e5e7eb);
  align-items: center;
  font-size: 0.875rem;
}

.table-row:last-child {
  border-bottom: none;
}

.col-workflow,
.col-user {
  font-weight: 500;
  color: var(--text-primary, #1f2937);
  display: flex;
  align-items: center;
}

.col-executions,
.col-operations,
.col-tokens {
  color: var(--text-secondary, #6b7280);
}

.col-cost {
  font-weight: 500;
  color: var(--text-primary, #1f2937);
}

.percentage-bar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.percentage-fill {
  height: 8px;
  background: linear-gradient(135deg, #6366f1, #8b5cf6);
  border-radius: 4px;
  flex: 1;
  max-width: 100px;
}

.percentage-bar span {
  font-size: 0.75rem;
  color: var(--text-secondary, #6b7280);
  white-space: nowrap;
}

.chart-container {
  min-height: 320px;
}

/* Details Tab Styles */
.details-filters {
  margin-bottom: 1.5rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid var(--stroke, #e5e7eb);
}

.filter-row {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  align-items: flex-end;
}

.filter-group {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  min-width: 150px;
}

.filter-label {
  font-size: 0.75rem;
  font-weight: 500;
  color: var(--text-secondary, #6b7280);
  text-transform: uppercase;
}

.filter-input {
  height: 36px;
}

.filter-select {
  height: 36px;
  padding: 0 0.75rem;
  border: 1px solid var(--stroke, #e5e7eb);
  border-radius: 0.375rem;
  font-size: 0.875rem;
  background: var(--bg-1);
  color: var(--text-primary, #1f2937);
}

.filter-select:focus {
  outline: none;
  border-color: #6366f1;
  box-shadow: 0 0 0 2px rgba(99, 102, 241, 0.2);
}

.filter-actions {
  display: flex;
  gap: 0.5rem;
  margin-left: auto;
}

.apply-btn {
  background: var(--primary);
  color: var(--primary-foreground);
}

.apply-btn:hover {
  opacity: 0.9;
}

.loading-state.compact {
  min-height: 200px;
}

.details-table-container {
  overflow-x: auto;
}

.details-table {
  width: 100%;
  min-width: 800px;
}

.details-header {
  display: grid;
  grid-template-columns: 32px 130px 90px 1fr 1fr 1.5fr 90px 90px;
  gap: 0.5rem;
  padding: 0.75rem 1rem;
  background: var(--bg-1, #f3f4f6);
  border-radius: 0.5rem;
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--text-secondary, #6b7280);
  text-transform: uppercase;
}

.details-row {
  display: grid;
  grid-template-columns: 32px 130px 90px 1fr 1fr 1.5fr 90px 90px;
  gap: 0.5rem;
  padding: 0.875rem 1rem;
  border-bottom: 1px solid var(--stroke, #e5e7eb);
  align-items: center;
  font-size: 0.875rem;
  cursor: pointer;
  transition: background 0.15s;
}

.details-row:hover {
  background: var(--bg-1, #f9fafb);
}

.col-expand {
  display: flex;
  align-items: center;
  justify-content: center;
}

.expand-button {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 4px;
  border-radius: 4px;
  cursor: pointer;
}

.expand-icon {
  font-size: 0.75rem;
  color: var(--text-muted, #9ca3af);
  transition: transform 0.15s;
}

.col-date {
  color: var(--text-secondary, #6b7280);
  font-size: 0.8125rem;
}

.col-model {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
}

.model-provider {
  font-size: 0.75rem;
  color: var(--text-muted, #9ca3af);
}

.model-id {
  font-weight: 500;
  color: var(--text-primary, #1f2937);
  font-size: 0.8125rem;
}

.col-context {
  overflow: hidden;
}

.context-workflow {
  font-weight: 500;
  color: var(--text-primary, #1f2937);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.context-text {
  color: var(--text-secondary, #6b7280);
  font-size: 0.8125rem;
}

.col-description {
  overflow: hidden;
}

.description-text {
  display: block;
  color: var(--text-secondary, #6b7280);
  font-size: 0.8125rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.col-units {
  text-align: right;
  font-weight: 500;
  color: var(--text-primary, #1f2937);
}

.col-cost {
  text-align: right;
  font-weight: 600;
  color: var(--text-primary, #1f2937);
}

.details-expanded {
  padding: 1rem 1rem 1rem 3rem;
  background: var(--bg-1, #f9fafb);
  border-bottom: 1px solid var(--stroke, #e5e7eb);
}

.components-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
}

.component-item {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  padding: 0.75rem;
  background: var(--bg-1);
  border: 1px solid var(--stroke, #e5e7eb);
  border-radius: 0.5rem;
  min-width: 140px;
}

.component-values {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
}

.component-units {
  font-weight: 500;
  color: var(--text-primary, #1f2937);
}

.component-cost {
  font-size: 0.75rem;
  color: var(--text-secondary, #6b7280);
}

.expanded-meta {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-top: 1rem;
  padding-top: 0.75rem;
  border-top: 1px solid var(--stroke, #e5e7eb);
  font-size: 0.8125rem;
  color: var(--text-secondary, #6b7280);
}

.pagination-controls {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 1.5rem;
  padding-top: 1rem;
  border-top: 1px solid var(--stroke, #e5e7eb);
}

.pagination-info {
  font-size: 0.875rem;
  color: var(--text-secondary, #6b7280);
}

.pagination-buttons {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.page-indicator {
  font-size: 0.875rem;
  color: var(--text-secondary, #6b7280);
}

@media (max-width: 768px) {
  .filter-row {
    flex-direction: column;
    align-items: stretch;
  }

  .filter-group {
    min-width: 100%;
  }

  .filter-actions {
    margin-left: 0;
    justify-content: flex-end;
  }

  .details-header,
  .details-row {
    grid-template-columns: 24px 100px 80px 1fr 80px 80px;
  }

  .col-context,
  .col-description {
    display: none;
  }
}
</style>
