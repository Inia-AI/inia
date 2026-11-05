import { h, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ColumnDef, Row } from '@tanstack/vue-table'

import HighlightedText from '@/components/atoms/HighlightedText/HighlightedText.vue'
import type { Counterparty } from '@/api/schema'
import { isUuid } from '@/utils/uuid'
import { tableActionButton } from './tableActionButton'

export type CounterpartyRow = Counterparty & {
  type?: string
  parentName?: string
  searchQuery?: string
  isContextParent?: boolean
}

type RowAction = (row: Row<CounterpartyRow>, event: Event) => void

interface Options {
  onAdd: RowAction
  onEdit: RowAction
  onDelete: RowAction
  toggleRow: (row: Row<CounterpartyRow>) => void
  expandedRowIds: { has: (id: Counterparty['id']) => boolean }
  counterpartiesWithChild?: Ref<Counterparty['id'][] | undefined>
}

const truncateUuid = (id: string): string => `${id.slice(0, 8)}…${id.slice(-4)}`

const formatIdForDisplay = (id: string, search: string): string => {
  if (!isUuid(id)) return id
  const truncated = truncateUuid(id)
  const q = search.trim().toLowerCase()
  if (!q) return truncated
  const inFull = id.toLowerCase().includes(q)
  const inTruncated = truncated.toLowerCase().includes(q)
  return inFull && !inTruncated ? id : truncated
}

const copyToClipboard = (text: string) => {
  if (typeof navigator !== 'undefined' && navigator.clipboard) {
    navigator.clipboard.writeText(text).catch(() => {})
  }
}

export default ({
  onAdd,
  onEdit,
  onDelete,
  toggleRow,
  expandedRowIds,
  counterpartiesWithChild,
}: Options) => {
  const { t } = useI18n()
  const actionLabel = (action: string, row: Row<CounterpartyRow>) =>
    t(`page.counterpartyManagement.actions.${action}`, { name: row.original.name })

  const columns: ColumnDef<CounterpartyRow>[] = [
    {
      accessorKey: 'id',
      header: 'ID',
      size: 200,
      enableSorting: true,
      cell: ({ row }) => {
        const id = row.getValue('id') as string
        const isExpanded = expandedRowIds.has(row.original.id)
        const hasChildren =
          !row.original.parentId &&
          counterpartiesWithChild?.value?.includes(row.original.id)
        const search = row.original.searchQuery ?? ''
        const display = formatIdForDisplay(id, search)

        return h('div', { class: 'id-cell' }, [
          row.original.isContextParent
            ? h('span', { class: 'sr-only' }, t('page.counterpartyManagement.contextRowAria'))
            : null,
          hasChildren
            ? tableActionButton({
                label: actionLabel(isExpanded ? 'collapseRow' : 'expandRow', row),
                class: 'expand-chevron',
                icon: isExpanded ? 'chevron-down' : 'chevron-right',
                onActivate: () => toggleRow(row),
              })
            : h('span', { class: 'expand-chevron-spacer' }),
          h(
            'span',
            { class: 'id-cell-value', title: id },
            [h(HighlightedText, { text: display, query: search })],
          ),
          tableActionButton({
            label: actionLabel('copyId', row),
            class: 'id-copy',
            icon: 'copy',
            onActivate: () => copyToClipboard(id),
          }),
        ])
      },
    },
    {
      accessorKey: 'name',
      header: 'Name',
      enableSorting: true,
      cell: ({ row }) => {
        const name = row.getValue('name') as string
        const search = row.original.searchQuery ?? ''
        const parentName = row.original.parentName
        return h('div', { class: 'name-cell' }, [
          h(HighlightedText, { text: name, query: search }),
          parentName
            ? h('div', { class: 'name-cell-parent' }, [
                '↳ ',
                h(HighlightedText, { text: parentName, query: search }),
              ])
            : null,
        ])
      },
    },
    {
      accessorKey: 'nameVariants',
      header: 'Name Variants',
      cell: ({ row }) => {
        const value = (row.getValue('nameVariants') as string) || ''
        const search = row.original.searchQuery ?? ''
        return h(HighlightedText, { text: value, query: search })
      },
    },
    {
      accessorKey: 'type',
      header: 'Type',
      cell: ({ row }) => h('div', {}, (row.getValue('type') as string) || ''),
    },
    {
      id: 'actions',
      header: () => h('div', { class: 'text-right w-full' }, 'Actions'),
      cell: ({ row }) =>
        h(
          'div',
          { class: 'flex justify-end items-center gap-3 text-base actions-cell' },
          [
            tableActionButton({
              label: actionLabel('edit', row),
              class: 'action-icon action-icon-edit',
              icon: 'pencil',
              onActivate: event => onEdit(row, event),
            }),
            row.original.parentId === null
              ? tableActionButton({
                  label: actionLabel('addChild', row),
                  class: 'action-icon action-icon-add',
                  icon: 'plus',
                  onActivate: event => onAdd(row, event),
                })
              : null,
            tableActionButton({
              label: actionLabel('delete', row),
              class: 'action-icon action-icon-delete',
              icon: 'trash-can',
              onActivate: event => onDelete(row, event),
            }),
          ],
        ),
    },
  ]

  return { columns }
}
