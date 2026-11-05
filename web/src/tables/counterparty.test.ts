import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h, ref } from 'vue'

import DataTable from '@/components/organisms/DataTable/DataTable.vue'
import { tablePlugins } from '@/test/helpers/rowControls'
import useCounterpartyTable, { type CounterpartyRow } from './counterparty'

const row = (id: string, name: string, parentId: string | null): CounterpartyRow => ({
  id,
  name,
  parentId,
  nameVariants: '',
  counterpartyTypeIds: [],
})

const mountTable = (expanded: string[] = []) =>
  mount(
    defineComponent({
      setup() {
        const { columns } = useCounterpartyTable({
          onAdd: vi.fn(),
          onEdit: vi.fn(),
          onDelete: vi.fn(),
          toggleRow: vi.fn(),
          expandedRowIds: { has: id => expanded.includes(id) },
          counterpartiesWithChild: ref(['fund-1']),
        })
        return () =>
          h(DataTable, {
            columns,
            data: [row('fund-1', 'Pinebrook Capital', null), row('fund-2', 'Peer A', 'fund-1')],
          })
      },
    }),
    { global: { plugins: tablePlugins(), stubs: { IniaIcon: true } } },
  )

const actionNamesByRow = (wrapper: ReturnType<typeof mountTable>) =>
  wrapper.findAll('tbody tr').map(tr => tr.findAll('button').map(button => button.attributes('aria-label')))

describe('counterparty table row actions', () => {
  it('names the counterparty in every row action', () => {
    expect(actionNamesByRow(mountTable())).toEqual([
      [
        'Expand company Pinebrook Capital to show its children',
        'Copy ID of company Pinebrook Capital',
        'Edit company Pinebrook Capital',
        'Add child to company Pinebrook Capital',
        'Delete company Pinebrook Capital',
      ],
      ['Copy ID of company Peer A', 'Edit company Peer A', 'Delete company Peer A'],
    ])
  })

  it('names the counterparty whose children an expanded row hides', () => {
    expect(actionNamesByRow(mountTable(['fund-1']))[0]![0]).toBe('Collapse the children of company Pinebrook Capital')
  })

  it('shows the same name as a tooltip', () => {
    const wrapper = mountTable()
    for (const button of wrapper.findAll('tbody button')) {
      expect(button.attributes('title')).toBe(button.attributes('aria-label'))
    }
  })
})
