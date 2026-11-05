import { h } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ColumnDef, Row } from '@tanstack/vue-table'

import { ModelPricingListItem } from '@/api/modelPricing/schema'
import { tableActionButton } from './tableActionButton'

type RowAction = (row: Row<ModelPricingListItem>, event: Event) => void

interface TableOptions {
  onEdit?: RowAction
  onDelete?: RowAction
}

export default (options: TableOptions = {}) => {
  const { onEdit, onDelete } = options
  const { t } = useI18n()

  const columns: ColumnDef<ModelPricingListItem>[] = [
    {
      accessorKey: 'provider',
      header: 'Provider',
      enableSorting: true,
      cell: ({ row }) =>
        h('div', { class: 'font-medium' }, row.getValue('provider')),
    },
    {
      accessorKey: 'family',
      header: 'Family',
      enableSorting: true,
      cell: ({ row }) =>
        h('div', {}, row.getValue('family') || '-'),
    },
    {
      accessorKey: 'modelId',
      header: 'Model ID',
      enableSorting: true,
      cell: ({ row }) =>
        h('div', { class: 'font-mono text-sm' }, row.getValue('modelId')),
    },
    {
      accessorKey: 'displayName',
      header: 'Display Name',
      enableSorting: true,
      cell: ({ row }) => h('div', {}, row.getValue('displayName')),
    },
    {
      accessorKey: 'pricingType',
      header: 'Type',
      enableSorting: true,
      cell: ({ row }) => {
        const type = row.getValue('pricingType') as string
        const isLlm = type === 'Llm'
        return h(
          'span',
          {
            class: isLlm
              ? 'px-2 py-1 rounded-full text-xs bg-blue-100 text-blue-800'
              : 'px-2 py-1 rounded-full text-xs bg-purple-100 text-purple-800',
          },
          isLlm ? 'LLM' : 'Embedding',
        )
      },
    },
    {
      accessorKey: 'isActive',
      header: 'Status',
      cell: ({ row }) =>
        h(
          'div',
          {},
          row.getValue('isActive')
            ? h('span', { class: 'text-success' }, 'Active')
            : h('span', { class: 'text-error' }, 'Inactive'),
        ),
    },
    {
      id: 'actions',
      header: () => h('div', { class: 'text-right w-full' }, 'Actions'),
      cell: ({ row }) =>
        h('div', { class: 'flex justify-end items-center gap-3 text-lg' }, [
          onEdit &&
            tableActionButton({
              label: t('page.modelPricingManagement.actions.edit', { name: row.original.displayName }),
              icon: 'pencil',
              iconClass: 'text-info',
              onActivate: event => onEdit(row, event),
            }),
          onDelete &&
            tableActionButton({
              label: t('page.modelPricingManagement.actions.delete', { name: row.original.displayName }),
              icon: 'trash-can',
              iconClass: 'text-error',
              onActivate: event => onDelete(row, event),
            }),
        ]),
    },
  ]

  return { columns }
}
