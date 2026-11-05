<script setup lang="ts">
import type { Column, ColumnDef, Row } from '@tanstack/vue-table'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { FlexRender, getCoreRowModel, useVueTable } from '@tanstack/vue-table'
import { computed, PropType, useModel } from 'vue'
import { RouterLink, useRouter, type RouteLocationRaw } from 'vue-router'
import PaginationView from '@/components/molecules/PaginationView/PaginationView.vue'
import SortableHeader from '@/components/molecules/SortableHeader/SortableHeader.vue'

type DataRow = Row<any>

const props = defineProps({
  columns: {
    type: Array as PropType<ColumnDef<any>[]>,
    default: () => [],
    required: true,
  },
  rowTo: {
    type: Function as PropType<(row: DataRow) => RouteLocationRaw>,
    default: undefined,
  },
  rowLinkColumn: {
    type: String,
    default: undefined,
  },
  pageSize: {
    type: Number,
    default: 50,
  },
  totalCount: {
    type: Number,
    default: 0,
  },
  page: {
    type: Number,
    default: 0,
  },
  orderBy: {
    type: String,
    default: '',
  },
  rowClasses: {
    type: Object,
    default: () => {},
  },
  // The default cell padding is sized for sparse admin tables; a dense scan surface
  // (long lists read row by row) opts into tighter rows here instead of reaching into
  // the shared cells with :deep overrides.
  dense: {
    type: Boolean,
    default: false,
  },
  // The id of the one column that takes the table's spare width. Every other column keeps its
  // size, which TanStack defaults to 150px when a column sets none.
  fillColumn: {
    type: String,
    default: '',
  },
})

defineEmits(['update:page', 'update:orderBy'])
const pageModel = useModel(props, 'page')
const orderByModel = useModel(props, 'orderBy')

const data = defineModel<any[]>('data')

const table = useVueTable({
  data: data,
  columns: props.columns,
  getCoreRowModel: getCoreRowModel(),
  state: {
    get sorting() {
      if (!orderByModel.value) return []
      const [field, direction] = orderByModel.value.split(' ')
      if (!field) return []
      return [{ id: field, desc: direction === 'desc' }]
    },
  },
  manualPagination: true,
  pageCount: Math.ceil(props.totalCount / props.pageSize),
})

const router = props.rowTo ? useRouter() : undefined

const linkColumnId = computed(
  () => props.rowLinkColumn ?? table.getVisibleLeafColumns().find(column => column.id !== 'select')?.id,
)

const INTERACTIVE_TARGET = 'a, button, input, select, textarea, label, [role="button"]'

const openRow = (row: DataRow, event: MouseEvent) => {
  if (!props.rowTo || !router || (event.target as Element | null)?.closest(INTERACTIVE_TARGET)) {
    return
  }
  const to = props.rowTo(row)
  const wantsNewTab = event.button === 1 || (event.button === 0 && (event.ctrlKey || event.metaKey))
  if (wantsNewTab) {
    window.open(router.resolve(to).href, '_blank', 'noopener')
  } else if (event.button === 0) {
    void router.push(to)
  }
}

const headerStyle = (columnId: string, size: number | undefined) =>
  columnId === props.fillColumn ? undefined : { width: `${size}px` }

const toggleSort = (column: Pick<Column<unknown>, 'id' | 'getIsSorted'>) => {
  orderByModel.value = `${column.id} ${column.getIsSorted() === 'asc' ? 'desc' : 'asc'}`
}
</script>

<template>
  <div class="w-full">
    <Table class="border border-gray-200">
      <TableHeader class="bg-surface">
        <TableRow
          class="hover:bg-transparent"
          v-for="headerGroup in table.getHeaderGroups()"
          :key="headerGroup.id"
        >
          <template v-for="header in headerGroup.headers" :key="header.id">
            <SortableHeader
              v-if="header.column.columnDef.enableSorting"
              :as="TableHead"
              fill
              :direction="header.column.getIsSorted()"
              :style="headerStyle(header.column.id, header.column.columnDef.size)"
              :class="dense && 'h-9 px-3'"
              @sort="toggleSort(header.column)"
            >
              <FlexRender
                v-if="!header.isPlaceholder"
                :render="header.column.columnDef.header"
                :props="header.getContext()"
              />
            </SortableHeader>
            <TableHead
              v-else
              :style="headerStyle(header.column.id, header.column.columnDef.size)"
              :class="dense && 'h-9 px-3'"
            >
              <div class="flex items-center gap-2">
                <FlexRender
                  v-if="!header.isPlaceholder"
                  :render="header.column.columnDef.header"
                  :props="header.getContext()"
                />
              </div>
            </TableHead>
          </template>
        </TableRow>
      </TableHeader>
      <TableBody :key="JSON.stringify(data)">
        <template v-if="table.getRowModel().rows?.length">
          <template v-for="row in table.getRowModel().rows" :key="row.id">
            <!-- eslint-disable-next-line vue/no-restricted-syntax -- the link in the row's first cell is the keyboard and new-tab path; a click elsewhere in the row repeats it for the mouse -->
            <TableRow
              :class="[
                props.rowTo ? 'cursor-pointer' : '',
                rowClasses && rowClasses[row?.original?.id],
              ]"
              @click="openRow(row, $event)"
              @auxclick="openRow(row, $event)"
            >
              <TableCell v-for="cell in row.getVisibleCells()" :key="cell.id" :class="dense && 'px-3 py-2'">
                <RouterLink
                  v-if="props.rowTo && cell.column.id === linkColumnId"
                  :to="props.rowTo(row)"
                  :aria-label="cell.column.columnDef.meta?.rowLinkLabel?.(row)"
                  class="data-table-row-link focus-ring"
                >
                  <FlexRender :render="cell.column.columnDef.cell" :props="cell.getContext()" />
                </RouterLink>
                <FlexRender
                  v-else
                  :render="cell.column.columnDef.cell"
                  :props="cell.getContext()"
                />
              </TableCell>
            </TableRow>
          </template>
        </template>

        <TableRow v-else>
          <!-- TODO: move to i18n -->
          <TableCell :colspan="props.columns?.length" class="h-24 text-center">
            No results.
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>

    <div class="flex items-center justify-end space-x-2 py-4">
      <PaginationView
        :total-count="totalCount"
        :page-size="pageSize"
        :page="pageModel"
        @page-change="pageModel = $event"
      />
    </div>
  </div>
</template>

<style scoped>
.data-table-row-link {
  display: block;
  color: inherit;
  text-decoration: none;
}
</style>
