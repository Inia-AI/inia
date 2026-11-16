import { computed, h, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ColumnDef, Row } from '@tanstack/vue-table'

import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import { Checkbox } from '@/components/ui/checkbox'
import { formatDateTime } from '@/lib/utils'
import { formatBytes } from '@/lib/formatBytes'
import { formatMediaType, mediaTypeIconName, previewKindOf } from '@/lib/formatMediaType'
import type { DocumentSummary } from '@/api/documents/schema'
import { tableActionButton } from './tableActionButton'

export type AllSelectedState = 'off' | 'on' | 'mixed'

/**
 * Rows fed to the Documents table are the API `DocumentSummary` augmented with
 * pre-resolved display names (built in the view from the categories/users
 * queries). Putting resolution at the row level keeps the cell renderers
 * reactive when the resolution maps load.
 */
export type DocumentRow = DocumentSummary & {
  categoryName: string
  userName: string
  workflowTypeName: string | null
}

interface Options {
  selection: Ref<Set<string>>
  allSelected: ComputedRef<AllSelectedState>
  onToggleRow: (id: string) => void
  onToggleAll: () => void
  onPreview: (row: DocumentRow, event: Event) => void
  onDownload: (row: DocumentRow, event: Event) => void
  onEdit: (row: DocumentRow, event: Event) => void
  onDelete: (row: DocumentRow, event: Event) => void
  canDeleteDocuments: Ref<boolean>
  canSelectAny: Ref<boolean>
  canDelete: (row: DocumentRow) => boolean
}

export default ({
  selection,
  allSelected,
  onToggleRow,
  onToggleAll,
  onPreview,
  onDownload,
  onEdit,
  onDelete,
  canDeleteDocuments,
  canSelectAny,
  canDelete,
}: Options) => {
  const { t } = useI18n()
  const column = (key: string) => t(`page.documents.table.${key}`)
  const rowAction = (
    row: DocumentRow,
    action: 'preview' | 'download' | 'edit' | 'delete',
    icon: string,
    iconClass: string,
    handler: (row: DocumentRow, event: Event) => void,
  ) =>
    tableActionButton({
      label: t(`page.documents.actions.${action}`, { name: row.name }),
      icon,
      iconClass,
      onActivate: event => handler(row, event),
    })

  const selectColumn: ColumnDef<DocumentRow> = {
    id: 'select',
    size: 40,
    enableSorting: false,
    header: () =>
      h(
        'div',
        { class: 'flex items-center' },
        [
          h(Checkbox, {
            'aria-label': column('selectAll'),
            disabled: !canSelectAny.value,
            modelValue:
              allSelected.value === 'on'
                ? true
                : allSelected.value === 'mixed'
                  ? 'indeterminate'
                  : false,
            'onUpdate:modelValue': () => onToggleAll(),
          }),
        ],
      ),
    cell: ({ row }: { row: Row<DocumentRow> }) =>
      canDelete(row.original)
        ? h(
            'label',
            { class: 'flex items-center' },
            [
              h(Checkbox, {
                'aria-label': t('page.documents.table.selectRow', { name: row.original.name }),
                modelValue: selection.value.has(row.original.id),
                'onUpdate:modelValue': () => onToggleRow(row.original.id),
              }),
            ],

          )
        : null,
  }

  const columns = computed<ColumnDef<DocumentRow>[]>(() => [
    ...(canDeleteDocuments.value ? [selectColumn] : []),
    {
      accessorKey: 'name',
      header: () => column('name'),
      enableSorting: true,
      cell: ({ row }: { row: Row<DocumentRow> }) =>
        h('div', { class: 'document-name-cell' }, [
          h(IniaIcon, {
            name: mediaTypeIconName(row.original.mediaType),
            classes: 'document-name-icon',
          }),
          h(
            'span',
            { class: 'document-name-text', title: row.original.name },
            row.original.name,
          ),
        ]),
    },
    {
      accessorKey: 'mediaType',
      header: () => column('type'),
      enableSorting: true,
      cell: ({ row }: { row: Row<DocumentRow> }) =>
        h('span', {}, formatMediaType(row.original.mediaType)),
    },
    {
      accessorKey: 'documentCategoryId',
      header: () => column('category'),
      enableSorting: true,
      cell: ({ row }: { row: Row<DocumentRow> }) =>
        h('span', {}, row.original.categoryName),
    },
    {
      accessorKey: 'userId',
      header: () => column('user'),
      enableSorting: true,
      cell: ({ row }: { row: Row<DocumentRow> }) =>
        h('span', {}, row.original.userName),
    },
    {
      accessorKey: 'workflowTypeId',
      header: () => column('static'),
      enableSorting: false,
      cell: ({ row }: { row: Row<DocumentRow> }) =>
        row.original.workflowTypeName
          ? h('span', { class: 'document-static', title: row.original.class ?? undefined }, [
              h(IniaIcon, { name: 'thumbtack', classes: 'document-static-icon' }),
              row.original.workflowTypeName,
            ])
          : h('span', { class: 'document-static-none' }, '—'),
    },
    {
      accessorKey: 'sizeBytes',
      header: () => h('div', { class: 'text-right' }, column('size')),
      enableSorting: true,
      cell: ({ row }: { row: Row<DocumentRow> }) =>
        h('div', { class: 'text-right tabular-nums' }, formatBytes(row.original.sizeBytes)),
    },
    {
      accessorKey: 'createdAt',
      header: () => column('created'),
      enableSorting: true,
      cell: ({ row }: { row: Row<DocumentRow> }) =>
        h('span', { class: 'tabular-nums' }, formatDateTime(row.original.createdAt)),
    },
    {
      id: 'actions',
      enableSorting: false,
      header: () => h('div', { class: 'text-right w-full' }, column('actions')),
      cell: ({ row }: { row: Row<DocumentRow> }) =>
        h('div', { class: 'flex justify-end items-center gap-3 text-lg' }, [
          previewKindOf(row.original.mediaType)
            ? rowAction(row.original, 'preview', 'eye', 'text-muted-foreground hover:text-foreground', onPreview)
            : null,
          rowAction(row.original, 'download', 'download', 'text-info hover:opacity-80', onDownload),
          rowAction(row.original, 'edit', 'pencil', 'text-muted-foreground hover:text-foreground', onEdit),
          canDelete(row.original)
            ? rowAction(row.original, 'delete', 'trash-can', 'text-error hover:opacity-80', onDelete)
            : null,
        ]),
    },
  ])

  return { columns }
}
