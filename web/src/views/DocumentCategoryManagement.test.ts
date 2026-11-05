import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'

import { EN } from '@/assets/locales/en'
import { EDITOR_ROLES, ROLES } from '@/constants/roles'
import router from '@/plugins/router'
import DocumentCategoryManagement from './DocumentCategoryManagement.vue'

const CATEGORIES = [
  { id: 'legal-agreement', name: 'Legal Agreement', displayOrder: 2, createdAt: '' },
  { id: 'company-report', name: 'Company Report', displayOrder: 1, createdAt: '' },
]

vi.mock('@/api', async () => {
  const { ref: vueRef } = await vi.importActual<typeof import('vue')>('vue')
  const mutation = () => ({ mutateAsync: vi.fn(), isPending: vueRef(false) })
  return {
    default: {
      DocumentCategories: {
        useGetDocumentCategories: () => ({ data: vueRef(CATEGORIES), isLoading: vueRef(false), refetch: vi.fn() }),
        useCreateDocumentCategory: mutation,
        useUpdateDocumentCategory: mutation,
        useDeleteDocumentCategory: mutation,
      },
    },
  }
})

let wrapper: VueWrapper | null = null

afterEach(() => {
  wrapper?.unmount()
  wrapper = null
})

const mountPage = async () => {
  wrapper = mount(DocumentCategoryManagement, {
    global: {
      plugins: [createI18n({ legacy: false, locale: 'en', messages: { en: EN }, missingWarn: false })],
      stubs: { 'font-awesome-icon': true, DocumentCategoryForm: true },
    },
  })
  await flushPromises()
  return wrapper
}

const rowActions = () =>
  wrapper!.findAll('tbody tr').map(row => ({
    category: row.get('td').text(),
    names: row.findAll('.actions-column button').map(button => button.attributes('aria-label')),
  }))

describe('DocumentCategoryManagement row actions', () => {
  it('names each row\'s edit and delete buttons by the action and the category', async () => {
    await mountPage()

    expect(rowActions()).toEqual([
      { category: 'company-report', names: ['Edit company-report', 'Delete company-report'] },
      { category: 'legal-agreement', names: ['Edit legal-agreement', 'Delete legal-agreement'] },
    ])
  })

  it('offers one button whose name starts with Delete per category row', async () => {
    await mountPage()

    const deletes = wrapper!.findAll('button').filter(button => button.attributes('aria-label')?.startsWith('Delete'))

    expect(deletes).toHaveLength(CATEGORIES.length)
  })
})

describe('DocumentCategoryManagement subtitle', () => {
  const AUDIENCE_BY_ROLE: Record<string, string> = {
    [ROLES.WORKFLOW_EDITOR]: 'workflow editors',
    [ROLES.SUPER_ADMIN]: 'super admins',
  }

  it('names every role the route admits, and claims no narrower gate', async () => {
    await mountPage()
    const allowedRoles = router.resolve('/document-category-management').meta.allowedRoles as readonly string[]
    const subtitle = wrapper!.get('.page-subtitle').text()

    expect(allowedRoles).toEqual(EDITOR_ROLES)
    for (const role of allowedRoles) {
      expect(subtitle).toContain(AUDIENCE_BY_ROLE[role])
    }
    expect(subtitle).not.toMatch(/only/i)
  })
})
