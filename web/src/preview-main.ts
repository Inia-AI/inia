import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { createRouter, createWebHistory } from 'vue-router'
import { VueQueryPlugin } from '@tanstack/vue-query'
import registerPlugins from './register'
import { i18n } from './plugins'
import API from './api'
import {
  VERIFIER_ROLE,
  WORKER_ROLE,
  type AgentActivityBeat,
} from './api/agentActivity/schema'
import PreviewShell from './PreviewShell.vue'
import HomeView from './views/HomeView.vue'

const query = new URLSearchParams(location.search)
const idle = query.has('idle')
if (query.has('dark')) {
  document.documentElement.classList.add('dark')
}

const minutesAgo = (m: number) => new Date(Date.now() - m * 60_000).toISOString()

const beat = (
  atSeconds: number,
  label: string,
  kind: AgentActivityBeat['kind'] = 'Normal',
): AgentActivityBeat => ({ atSeconds, phaseIndex: 0, kind, label })

const digestOf = (
  startedAtUtc: string,
  beats: AgentActivityBeat[],
  outcome: 'Running' | 'Succeeded' | 'Failed',
  receipt: Partial<{ pagesViewed: number; codeRuns: number; durationSeconds: number }> = {},
) => ({
  schemaVersion: 1,
  outcome,
  startedAtUtc,
  phases: [],
  beats,
  receipt: {
    documentNames: [],
    skillNames: [],
    pagesViewed: receipt.pagesViewed ?? 0,
    codeRuns: receipt.codeRuns ?? 0,
    checksReported: 0,
    durationSeconds: receipt.durationSeconds ?? null,
  },
})

const amazonStart = minutesAgo(6)
const beaufortStart = minutesAgo(2)
const mixedStart = minutesAgo(12)

const liveReports = [
  {
    id: 'exec-amazon',
    title: 'Corporate credit analysis',
    counterpartyName: 'Amazon.com, Inc.',
    workflowState: 'Running',
    analystName: 'Priya Nair',
    createdAt: minutesAgo(7),
    startedAt: amazonStart,
    agentActivity: [
      {
        blockId: 'is',
        blockName: 'Income Statement',
        settled: false,
        activity: digestOf(amazonStart, [
          beat(12, 'Planning the extraction.'),
          beat(180, 'Layout matches text extraction. Statement is on page 36.'),
          beat(340, 'Reconciliation ties exactly for both periods.', 'Check'),
        ], 'Running', { pagesViewed: 1, codeRuns: 1 }),
      },
      {
        blockId: 'bs',
        blockName: 'Balance Sheet',
        settled: true,
        activity: digestOf(amazonStart, [], 'Failed'),
      },
      {
        blockId: 'cf',
        blockName: 'Cash Flow',
        settled: false,
        activity: digestOf(amazonStart, [
          beat(20, 'Planning the extraction.'),
          beat(95, 'Reading the notes to the financial statements.'),
        ], 'Running'),
      },
    ],
  },
  {
    id: 'exec-verified',
    title: 'Part 1 - Corporates',
    counterpartyName: 'Halden Chemicals AG',
    workflowState: 'Running',
    analystName: 'Priya Nair',
    createdAt: minutesAgo(9),
    startedAt: amazonStart,
    // A verification group mid-loop: one member passed, one is being redone after the
    // reviewer failed it, and the reviewer is judging again. The redone member carries both
    // its legs — the settled first round and the running second — which is what the desk has
    // to read as one agent working, not as a finished agent beside a running one.
    agentActivity: [
      {
        blockId: 'toc',
        blockName: 'TOC reader',
        round: 1,
        role: WORKER_ROLE,
        settled: true,
        activity: digestOf(amazonStart, [
          beat(30, 'Listing the section titles.'),
          beat(88, 'Every title matches the source document.', 'Check'),
        ], 'Succeeded', { durationSeconds: 88 }),
      },
      {
        blockId: 'summary',
        blockName: 'Doc summary',
        round: 1,
        role: WORKER_ROLE,
        settled: true,
        activity: digestOf(amazonStart, [
          beat(24, 'Reading the document front matter.'),
        ], 'Succeeded', { durationSeconds: 61 }),
      },
      {
        blockId: 'summary',
        blockName: 'Doc summary',
        round: 2,
        role: WORKER_ROLE,
        settled: false,
        activity: digestOf(beaufortStart, [
          beat(15, 'Re-reading the document to count its pages.'),
          beat(70, 'Stating the page count with the title.'),
        ], 'Running'),
      },
      {
        blockId: 'verifier',
        blockName: 'Statement verifier',
        round: 2,
        role: VERIFIER_ROLE,
        settled: false,
        activity: digestOf(beaufortStart, [
          beat(8, 'Reading both members’ output against the source.'),
        ], 'Running'),
      },
    ],
  },
  {
    id: 'exec-mixed',
    title: 'Broker dealer review',
    counterpartyName: 'Ashford Securities LLC',
    workflowState: 'Running',
    analystName: 'Daniel Okafor',
    createdAt: minutesAgo(13),
    startedAt: mixedStart,
    workflowActivity: {
      schemaVersion: 1,
      currentStepIndex: 2,
      steps: [
        { label: 'Reading the regulatory filings', status: 'Done' },
        { label: 'Summarising the ownership structure', status: 'Done' },
        { label: 'Extracting the financial statements', status: 'Active' },
        { label: 'Writing the credit summary', status: 'Pending' },
        { label: 'Building the report workbook', status: 'Pending' },
      ],
    },
    agentActivity: [
      {
        blockId: 'is-agt',
        blockName: 'Income Statement',
        settled: false,
        activity: digestOf(mixedStart, [
          beat(40, 'Planning the extraction.'),
          beat(210, 'Statement of operations is on page 4.'),
        ], 'Running', { pagesViewed: 1 }),
      },
      {
        blockId: 'bs-agt',
        blockName: 'Balance Sheet',
        settled: true,
        activity: digestOf(mixedStart, [
          beat(30, 'Planning the extraction.'),
          beat(300, 'Assets equal liabilities plus equity for both dates.', 'Check'),
        ], 'Succeeded', { durationSeconds: 410 }),
      },
    ],
  },
  {
    id: 'exec-classic',
    title: 'Part 1 - Corporates',
    counterpartyName: 'Corvane Paper Mills',
    workflowState: 'Running',
    analystName: 'Sofia Marchetti',
    createdAt: minutesAgo(5),
    startedAt: minutesAgo(4),
    workflowActivity: {
      schemaVersion: 1,
      currentStepIndex: 1,
      steps: [
        { label: 'Reading the annual report', status: 'Done' },
        { label: 'Extracting the income statement', status: 'Active' },
        { label: 'Extracting the balance sheet', status: 'Active' },
        { label: 'Writing the credit summary', status: 'Pending' },
      ],
    },
    agentActivity: [],
  },
  {
    id: 'exec-beaufort',
    title: 'Corporate credit analysis',
    counterpartyName: 'Beaufort Maritime Holdings',
    workflowState: 'ProcessingDocuments',
    analystName: 'Daniel Okafor',
    createdAt: beaufortStart,
    startedAt: beaufortStart,
    agentActivity: [],
  },
]

const settledBlock = (blockId: string, blockName: string) => ({
  blockId,
  blockName,
  settled: true,
  activity: null,
})

const threeAgents = [
  settledBlock('is', 'Income Statement'),
  settledBlock('bs', 'Balance Sheet'),
  settledBlock('cf', 'Cash Flow'),
]

const draftReports = [
  {
    id: 'draft-vantage',
    title: 'Corporate credit analysis',
    counterpartyName: 'Vantage Logistics Group',
    workflowState: 'Draft',
    analystName: 'Priya Nair',
    createdAt: minutesAgo(95),
    duration: '00:11:24',
    agentActivity: threeAgents,
  },
  {
    id: 'draft-meridian',
    title: 'Part 1 - Corporates',
    counterpartyName: 'Meridian Foods plc',
    workflowState: 'Draft',
    analystName: 'Daniel Okafor',
    createdAt: minutesAgo(60 * 3),
    duration: '00:09:02',
    agentActivity: threeAgents,
  },
  {
    id: 'draft-northgate',
    title: 'Corporate credit analysis',
    counterpartyName: 'Northgate Industrial',
    workflowState: 'Draft',
    analystName: 'Priya Nair',
    createdAt: minutesAgo(60 * 5),
    duration: '00:14:41',
    agentActivity: threeAgents,
  },
  {
    id: 'draft-halcyon',
    title: 'Part 1 - Corporates',
    counterpartyName: 'Halcyon Energy',
    workflowState: 'Draft',
    analystName: 'Sofia Marchetti',
    createdAt: minutesAgo(60 * 50),
    duration: '00:08:17',
    agentActivity: [
      settledBlock('is', 'Income Statement'),
      settledBlock('cf', 'Cash Flow'),
    ],
  },
]

const inFlight = idle ? [] : liveReports

type UnknownRecord = Record<string, unknown>

const previewTokenPayload = btoa(
  JSON.stringify({ preferred_username: 'jane.doe', sub: 'preview-user' }),
)
localStorage.setItem('accessToken', `preview.${previewTokenPayload}.preview`)

API.WorkflowReports.service.getWorkflowReports = (async (params?: UnknownRecord) => {
  const filter = String(params?.['$filter'] ?? '')
  if (filter.includes('Draft')) {
    return { value: draftReports, count: draftReports.length }
  }
  return { value: inFlight, count: inFlight.length }
}) as unknown as typeof API.WorkflowReports.service.getWorkflowReports

API.WorkflowReports.service.getWorkflowReport = (async (id: string) =>
  [...inFlight, ...draftReports].find(r => r.id === id) ??
  inFlight[0]) as unknown as typeof API.WorkflowReports.service.getWorkflowReport

API.Counterparties.service.getCounterparties = (async () => ({
  value: [],
})) as unknown as typeof API.Counterparties.service.getCounterparties

API.Users.service.resolveUsers = (async () => [
  { username: 'jane.doe', fullName: 'Jane Doe' },
]) as unknown as typeof API.Users.service.resolveUsers

API.WorkflowCategories.service.getWorkflowCategorySummaries = (async () =>
  []) as typeof API.WorkflowCategories.service.getWorkflowCategorySummaries

API.Meta.service.getVersion = (async () => ({
  version: '',
})) as unknown as typeof API.Meta.service.getVersion

API.AgentActivity.service.getBlockActivity = (async (blockId: string) => {
  for (const report of liveReports) {
    const block = report.agentActivity.find(b => b.blockId === blockId)
    if (block?.activity) {
      return block.activity
    }
  }
  return digestOf(amazonStart, [], 'Failed')
}) as unknown as typeof API.AgentActivity.service.getBlockActivity

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/preview.html', name: 'Home', alias: '/', component: HomeView },
    { path: '/workflow-reports', name: 'WorkflowReports', component: HomeView },
    { path: '/login', name: 'Login', component: HomeView },
    { path: '/profile', name: 'Profile', component: HomeView },
    { path: '/:pathMatch(.*)*', name: 'PreviewFallback', component: HomeView },
  ],
})

const app = createApp(PreviewShell)
app.use(createPinia())
app.use(router)
app.use(i18n)
app.use(VueQueryPlugin)
registerPlugins(app)
app.mount('#app')
