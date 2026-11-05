/**
 * Smoke tests for the Documents list view. Exhaustive interaction coverage
 * lives in the pure-function tests (`documentsQuery.test.ts`,
 * `useDocumentsQueryState.test.ts`). This file only asserts the view wires
 * them up correctly: renders empty state, emits a navigation on row click.
 */
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

// ---- mocks (hoisted) -----------------------------------------------------
const { pushMock } = vi.hoisted(() => ({ pushMock: vi.fn() }))
const { isAdminRef, isOperatorRef } = vi.hoisted(() => ({
  isAdminRef: { value: true },
  isOperatorRef: { value: false },
}))
const { documentsRef, categoriesRef, usersRef } = vi.hoisted(() => {
  // The mocks are instantiated lazily inside the factory below so that
  // `await import('vue')` inside the factory is allowed by Vitest's hoisting.
  return {
    documentsRef: { value: null as unknown },
    categoriesRef: { value: [] as unknown[] },
    usersRef: { value: { value: [] as unknown[] } as unknown },
  }
})

vi.mock('@/api', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  documentsRef.value = vueRef(null)
  categoriesRef.value = vueRef([])
  usersRef.value = vueRef({ value: [] })

  return {
    default: {
      Documents: {
        useGetDocuments: () => ({
          data: documentsRef.value,
          isLoading: vueRef(false),
        }),
        useBulkDeleteDocuments: () => ({
          mutateAsync: vi.fn(),
          isPending: vueRef(false),
        }),
        service: { getDocument: vi.fn() },
      },
      DocumentCategories: {
        useGetDocumentCategories: () => ({ data: categoriesRef.value }),
      },
      Users: {
        useGetUsers: () => ({ data: usersRef.value }),
      },
      Files: {
        useGetFile: () => ({ mutateAsync: vi.fn() }),
      },
      Workflows: {
        useGetWorkflowTypeOptions: () => ({
          data: vueRef([{ workflowTypeId: 'AssetManagerCreditAnalysis', workflowTypeName: 'Asset Manager Credit Analysis' }]),
        }),
      },
    },
  }
})

vi.mock('vue-i18n', async importOriginal => {
  const actual = await importOriginal<typeof import('vue-i18n')>()
  return {
    ...actual,
    useI18n: () => ({
      t: (key: string, params?: Record<string, unknown>) => {
        if (!params) return key
        return `${key}:${JSON.stringify(params)}`
      },
    }),
  }
})

vi.mock('@/components/ui/toast/use-toast', () => ({
  useToast: () => ({ toast: vi.fn() }),
}))

vi.mock('@/composables/useCurrentUserRoles', () => ({
  useCurrentUserRoles: () => ({
    isAdmin: isAdminRef,
    isOperator: isOperatorRef,
    isSuperAdmin: { value: false },
  }),
}))

vi.mock('vue-router', async () => {
  const actual = await vi.importActual<typeof import('vue-router')>('vue-router')
  return {
    ...actual,
    useRouter: () => ({ push: pushMock, replace: vi.fn() }),
  }
})

// Import after all mocks are in place.
import Documents from './DocumentsList.vue'

// Stub the preview dialog so the test asserts wiring (open + props) rather than
// the dialog's teleport/iframe internals.
const FilePreviewDialogStub = {
  name: 'FilePreviewDialog',
  props: {
    open: Boolean,
    fileId: String,
    filename: String,
    kind: String,
    unavailableText: String,
  },
  template:
    '<div v-if="open" data-test="preview-dialog">{{ kind }}|{{ fileId }}|{{ filename }}</div>',
}

// Helper: get at the mocked refs.
const state = () => ({
  documents: documentsRef.value as { value: unknown },
  categories: categoriesRef.value as { value: unknown },
  users: usersRef.value as { value: unknown },
})

beforeEach(() => {
  pushMock.mockReset()
  isAdminRef.value = true
  isOperatorRef.value = false
  state().documents.value = null
  state().categories.value = []
  state().users.value = { value: [] }
})

const ONE_ROW = {
  value: [
    {
      id: 'd1',
      name: 'invoice.pdf',
      mediaType: 'Pdf',
      sizeBytes: 1024,
      extension: '.pdf',
      userId: null,
      documentCategoryId: null,
      workflowTypeId: null,
      class: null,
      createdAt: '2026-04-22T10:00:00Z',
      updatedAt: null,
    },
  ],
  count: 1,
  skip: 0,
  top: 25,
}

const PREVIEWABLE_ROW = {
  value: [
    {
      id: 'd1',
      name: 'invoice.pdf',
      mediaType: 'ApplicationPdf',
      sizeBytes: 1024,
      extension: '.pdf',
      userId: null,
      documentCategoryId: null,
      workflowTypeId: null,
      class: null,
      createdAt: '2026-04-22T10:00:00Z',
      updatedAt: null,
    },
  ],
  count: 1,
  skip: 0,
  top: 25,
}

const NON_PREVIEWABLE_ROW = {
  value: [
    {
      id: 'd2',
      name: 'archive.zip',
      mediaType: 'ApplicationZip',
      sizeBytes: 2048,
      extension: '.zip',
      userId: null,
      documentCategoryId: null,
      workflowTypeId: null,
      class: null,
      createdAt: '2026-04-22T10:00:00Z',
      updatedAt: null,
    },
  ],
  count: 1,
  skip: 0,
  top: 25,
}

const actionTitle = (action: string, name: string) => `page.documents.actions.${action}:${JSON.stringify({ name })}`
const actionButton = (action: string, name: string) => `[title='${actionTitle(action, name)}']`

const makeRouter = (): Router =>
  createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/documents', component: Documents, name: 'Documents' },
      { path: '/documents/:id', component: { template: '<div/>' }, name: 'DocumentDetails' },
    ],
  })

const EditDocumentDialogStub = {
  name: 'EditDocumentDialog',
  props: { open: Boolean, document: Object },
  template: '<div v-if="open" data-test="edit-dialog">{{ document && document.id }}</div>',
}

const UploadDocumentsDialogStub = {
  name: 'UploadDocumentsDialog',
  props: { open: Boolean },
  template: '<div v-if="open" data-test="upload-dialog" />',
}

const STATIC_ROW = {
  value: [
    {
      ...ONE_ROW.value[0],
      id: 'd3',
      name: 'framework.pdf',
      workflowTypeId: 'AssetManagerCreditAnalysis',
      class: 'Regulatory_Fmk_Updated',
    },
  ],
  count: 1,
  skip: 0,
  top: 25,
}

const mountView = async (initialPath = '/documents') => {
  const router = makeRouter()
  router.push(initialPath)
  await router.isReady()
  const wrapper = mount(Documents, {
    global: {
      plugins: [router],
      stubs: {
        FilePreviewDialog: FilePreviewDialogStub,
        EditDocumentDialog: EditDocumentDialogStub,
        UploadDocumentsDialog: UploadDocumentsDialogStub,
      },
    },
  })
  await flushPromises()
  return { wrapper, router }
}

describe('Documents.vue', () => {
  it('names the workflow type a static document joins', async () => {
    state().documents.value = STATIC_ROW
    const { wrapper } = await mountView()

    const row = wrapper.findAll('tr')[1]
    expect(row.text()).toContain('Asset Manager Credit Analysis')
  })

  it('opens the edit dialog on the row it was clicked on, without opening the row', async () => {
    state().documents.value = STATIC_ROW
    const { wrapper, router } = await mountView()

    await wrapper.get(actionButton('edit', 'framework.pdf')).trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-test="edit-dialog"]').text()).toBe('d3')
    expect(router.currentRoute.value.name).toBe('Documents')
  })

  it('opens the upload dialog', async () => {
    state().documents.value = ONE_ROW
    const { wrapper } = await mountView()

    await wrapper.get('[data-testid="upload-documents"]').trigger('click')

    expect(wrapper.find('[data-test="upload-dialog"]').exists()).toBe(true)
  })

  it('renders the empty state when the API returns no rows', async () => {
    state().documents.value = { value: [], count: 0, skip: 0, top: 25 }
    const { wrapper } = await mountView()
    expect(wrapper.text()).toContain('page.documents.emptyText')
  })

  it('renders the table and navigates on row click', async () => {
    state().documents.value = ONE_ROW
    const { wrapper } = await mountView()
    expect(wrapper.text()).toContain('invoice.pdf')

    // Click the first non-header data row (TableRow).
    const rows = wrapper.findAll('tr')
    // rows[0] is the header. Skip to the first data row.
    expect(rows.length).toBeGreaterThan(1)
    await rows[1].trigger('click')

    expect(pushMock).toHaveBeenCalledWith({
      name: 'DocumentDetails',
      params: { id: 'd1' },
    })
  })

  it('opens a document from the keyboard through a link on its name, without the row navigating twice', async () => {
    state().documents.value = ONE_ROW
    const { wrapper, router } = await mountView()

    const link = wrapper.get('a[href="/documents/d1"]')
    expect(link.text()).toBe('invoice.pdf')

    await link.trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.name).toBe('DocumentDetails')
    expect(pushMock).not.toHaveBeenCalled()
  })

  it.each(['preview', 'download', 'edit', 'delete'])('does not open the row when its %s action is clicked', async action => {
    isAdminRef.value = true
    state().documents.value = PREVIEWABLE_ROW
    const { wrapper, router } = await mountView()

    await wrapper.get(actionButton(action, 'invoice.pdf')).trigger('click')
    await flushPromises()

    expect(pushMock).not.toHaveBeenCalled()
    expect(router.currentRoute.value.name).toBe('Documents')
  })

  it('shows the jobId chip when ?jobId is present', async () => {
    state().documents.value = { value: [], count: 0, skip: 0, top: 25 }
    const { wrapper } = await mountView('/documents?jobId=my-job')
    expect(wrapper.text()).toContain('page.documents.jobChip')
    expect(wrapper.text()).toContain('my-job')
  })

  it('hides the selection column and per-row delete icon for non-admin users', async () => {
    isAdminRef.value = false
    state().documents.value = ONE_ROW
    const { wrapper } = await mountView()

    expect(
      wrapper.find('[aria-label="page.documents.table.selectAll"]').exists(),
    ).toBe(false)
    expect(
      wrapper.find(actionButton('delete', 'invoice.pdf')).exists(),
    ).toBe(false)
    // Download icon must remain available to non-admins.
    expect(
      wrapper.find(actionButton('download', 'invoice.pdf')).exists(),
    ).toBe(true)
  })

  it('shows the per-row delete icon for an operator who is not an admin', async () => {
    isAdminRef.value = false
    isOperatorRef.value = true
    state().documents.value = ONE_ROW
    const { wrapper } = await mountView()

    expect(
      wrapper.find(actionButton('delete', 'invoice.pdf')).exists(),
    ).toBe(true)
  })

  it('shows the selection column and per-row delete icon for admin users', async () => {
    isAdminRef.value = true
    state().documents.value = ONE_ROW
    const { wrapper } = await mountView()

    expect(
      wrapper.find('[aria-label="page.documents.table.selectAll"]').exists(),
    ).toBe(true)
    expect(
      wrapper.find(actionButton('delete', 'invoice.pdf')).exists(),
    ).toBe(true)
  })

  it('shows a preview action for previewable (pdf/image) documents', async () => {
    state().documents.value = PREVIEWABLE_ROW
    const { wrapper } = await mountView()

    expect(
      wrapper.find(actionButton('preview', 'invoice.pdf')).exists(),
    ).toBe(true)
  })

  it('hides the preview action for non-previewable documents', async () => {
    state().documents.value = NON_PREVIEWABLE_ROW
    const { wrapper } = await mountView()

    expect(
      wrapper.find(actionButton('preview', 'archive.zip')).exists(),
    ).toBe(false)
    // Download must remain available regardless of preview support.
    expect(
      wrapper.find(actionButton('download', 'archive.zip')).exists(),
    ).toBe(true)
  })

  it('renders the preview action before download in the row', async () => {
    state().documents.value = PREVIEWABLE_ROW
    const { wrapper } = await mountView()

    const titles = wrapper
      .findAll('.table-action-button')
      .map(el => el.attributes('title'))
    expect(titles).toEqual(
      ['preview', 'download', 'edit', 'delete'].map(name => actionTitle(name, 'invoice.pdf')),
    )
  })

  it('names the document in every row action and in the row checkbox', async () => {
    state().documents.value = PREVIEWABLE_ROW
    const { wrapper } = await mountView()

    const row = wrapper.findAll('tr')[1]!
    expect(row.findAll('.table-action-button').map(el => el.attributes('aria-label'))).toEqual(
      ['preview', 'download', 'edit', 'delete'].map(name => actionTitle(name, 'invoice.pdf')),
    )
    expect(row.get('[role="checkbox"]').attributes('aria-label')).toBe(
      'page.documents.table.selectRow:{"name":"invoice.pdf"}',
    )
  })

  it('opens the preview dialog with the document file id and kind on click', async () => {
    state().documents.value = PREVIEWABLE_ROW
    const { wrapper } = await mountView()

    expect(wrapper.find('[data-test="preview-dialog"]').exists()).toBe(false)

    await wrapper.find(actionButton('preview', 'invoice.pdf')).trigger('click')
    await flushPromises()

    const dialog = wrapper.find('[data-test="preview-dialog"]')
    expect(dialog.exists()).toBe(true)
    expect(dialog.text()).toBe('pdf|d1|invoice.pdf')
  })
})
