import type { Row, RowData } from '@tanstack/vue-table'

declare module '@tanstack/vue-table' {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars -- a merged declaration must repeat the original's type parameters
  interface ColumnMeta<TData extends RowData, TValue> {
    rowLinkLabel?: (row: Row<TData>) => string | undefined
  }
}
