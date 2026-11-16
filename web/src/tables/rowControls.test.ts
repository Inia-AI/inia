import { vi } from 'vitest'
import { computed, ref } from 'vue'

import { documentDetailsRoute, reportRoute } from '@/lib/routes'
import { describeRowControls, REPORT_PAGE_ROUTE, type TableCase, type TableSetup } from '@/test/helpers/rowControls'
import { WorkflowState } from '@/enums'
import type { DocumentRow } from './documents'
import type { CounterpartyRow } from './counterparty'
import type { WorkflowReportSummary } from '@/api/schema'
import type { ModelPricingListItem } from '@/api/modelPricing/schema'
import useCounterpartyTable from './counterparty'
import useDocumentsTable from './documents'
import useFinancialAnalysisTable from './financialAnalysis'
import useModelPricingTable from './modelPricing'

const counterparty = (id: string, name: string, parentId: string | null): CounterpartyRow => ({
  id,
  name,
  parentId,
  nameVariants: '',
  counterpartyTypeIds: [],
})

const document = (id: string, name: string, createdAt: string): DocumentRow => ({
  id,
  name,
  mediaType: 'application/pdf',
  sizeBytes: 2048,
  extension: '.pdf',
  userId: 'u1',
  documentCategoryId: 'annual-report',
  workflowTypeId: null,
  class: null,
  processingState: 'Processed',
  processingError: null,
  createdAt,
  updatedAt: null,
  categoryName: 'Annual report',
  userName: 'Priya Nair',
  workflowTypeName: null,
})

const report = (id: string, createdAt: string): WorkflowReportSummary => ({
  id,
  createdAt,
  workflowState: WorkflowState.Draft,
  workflowTypeId: 'credit-memo',
  workflowTypeName: 'Credit Memo',
  counterpartyName: 'Pinebrook Capital',
  analystName: 'Quarterly review',
})

const pricing = (id: string, displayName: string): ModelPricingListItem => ({
  id,
  provider: 'Anthropic',
  modelId: id,
  displayName,
  pricingType: 'Llm',
  isActive: true,
})

const TABLES: Record<string, TableCase> = {
  './counterparty.ts': {
    table: () =>
      useCounterpartyTable({
        onAdd: vi.fn(),
        onEdit: vi.fn(),
        onDelete: vi.fn(),
        toggleRow: vi.fn(),
        expandedRowIds: { has: () => false },
        counterpartiesWithChild: ref(['fund-1']),
      }) as TableSetup,
    rows: [
      counterparty('fund-1', 'Pinebrook Capital', null),
      counterparty('fund-2', 'Peer A', 'fund-1'),
      counterparty('fund-3', 'Harbor Credit', null),
    ],
  },
  './documents.ts': {
    table: () => ({
      columns: useDocumentsTable({
        selection: ref(new Set<string>()),
        allSelected: computed(() => 'off' as const),
        onToggleRow: vi.fn(),
        onToggleAll: vi.fn(),
        onPreview: vi.fn(),
        onDownload: vi.fn(),
        onEdit: vi.fn(),
        onDelete: vi.fn(),
        canDeleteDocuments: ref(true),
        canSelectAny: ref(true),
        canDelete: () => true,
      }).columns.value,
    }) as TableSetup,
    rows: [
      document('d1', 'annual-report-2025.pdf', '2026-10-01T09:00:00Z'),
      document('d2', 'fund-letter.pdf', '2026-10-02T09:00:00Z'),
    ],
    rowTo: row => documentDetailsRoute((row.original as DocumentRow).id),
  },
  './financialAnalysis.ts': {
    table: () => useFinancialAnalysisTable() as unknown as TableSetup,
    rows: [report('r1', '2026-10-01T09:00:00Z'), report('r2', '2026-10-02T10:30:00Z')],
    rowTo: row => reportRoute((row.original as WorkflowReportSummary).id),
  },
  './modelPricing.ts': {
    table: () => useModelPricingTable({ onEdit: vi.fn(), onDelete: vi.fn() }) as TableSetup,
    rows: [pricing('claude-sonnet', 'Claude Sonnet'), pricing('claude-haiku', 'Claude Haiku')],
  },
}

describeRowControls(import.meta.glob(['./*.ts', '!./*.test.ts', '!./index.ts'], { eager: true }), TABLES, {
  routes: [REPORT_PAGE_ROUTE, { name: 'DocumentDetails', path: '/documents/:id', component: { render: () => null } }],
  stubs: { LiveDuration: true, WorkflowStatus: true, ReplayProvenanceBadge: true },
})
