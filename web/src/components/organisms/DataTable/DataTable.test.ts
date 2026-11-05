import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { h } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import type { ColumnDef } from '@tanstack/vue-table'

import DataTable from './DataTable.vue'

type Row = { id: string; name: string; size: number }

const makeRouter = (): Router =>
  createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/items', name: 'Items', component: { template: '<div/>' } },
      { path: '/items/:id', name: 'Item', component: { template: '<div/>' } },
    ],
  })

const columns: ColumnDef<Row>[] = [
  { accessorKey: 'name', header: 'Name', enableSorting: true },
  { accessorKey: 'size', header: 'Size', enableSorting: false },
]

const mountTable = (orderBy = '') =>
  mount(DataTable, {
    props: {
      columns,
      data: [{ id: 'r1', name: 'Annual report', size: 12 }],
      totalCount: 1,
      orderBy,
      'onUpdate:orderBy': (value: string) => wrapper.setProps({ orderBy: value }),
    },
    global: { plugins: [makeRouter()] },
    attachTo: document.body,
  })

let wrapper: ReturnType<typeof mountTable>

const sortableHeader = () => wrapper.findAll('th')[0]!
const sortButton = () => sortableHeader().get('button')

describe('DataTable sortable headers', () => {
  it('offers a sortable column as a native button in the tab order', () => {
    wrapper = mountTable()

    expect(sortButton().attributes('type')).toBe('button')
    expect(sortButton().text()).toBe('Name')
    ;(sortButton().element as HTMLButtonElement).focus()
    expect(document.activeElement).toBe(sortButton().element)
    wrapper.unmount()
  })

  it('announces the sort state on the header and reverses it on a second press', async () => {
    wrapper = mountTable()
    expect(sortableHeader().attributes('aria-sort')).toBe('none')

    await sortButton().trigger('click')
    expect(wrapper.emitted('update:orderBy')!.at(-1)).toEqual(['name asc'])
    expect(sortableHeader().attributes('aria-sort')).toBe('ascending')

    await sortButton().trigger('click')
    expect(wrapper.emitted('update:orderBy')!.at(-1)).toEqual(['name desc'])
    expect(sortableHeader().attributes('aria-sort')).toBe('descending')
    wrapper.unmount()
  })

  it('keeps keyboard focus on the header when the sorted rows arrive, so a second press can reverse the order', async () => {
    wrapper = mountTable()
    ;(sortButton().element as HTMLButtonElement).focus()

    await sortButton().trigger('click')
    await wrapper.setProps({ data: [{ id: 'r2', name: 'Board minutes', size: 3 }] })

    expect(document.activeElement).toBe(sortButton().element)
    expect(wrapper.text()).toContain('Board minutes')
    wrapper.unmount()
  })

  it('leaves a column that cannot sort as plain header text with no sort state', () => {
    wrapper = mountTable('name asc')

    const plainHeader = wrapper.findAll('th')[1]!
    expect(plainHeader.find('button').exists()).toBe(false)
    expect(plainHeader.attributes('aria-sort')).toBeUndefined()
    expect(sortableHeader().attributes('aria-sort')).toBe('ascending')
    wrapper.unmount()
  })
})

describe('DataTable rows that open a page', () => {
  const rowColumns: ColumnDef<Row>[] = [
    { accessorKey: 'name', header: 'Name', cell: ({ row }) => h('div', { class: 'name-cell' }, row.original.name) },
    { accessorKey: 'size', header: 'Size' },
    {
      id: 'actions',
      header: 'Actions',
      cell: () => [
        h('button', { type: 'button', class: 'delete-action' }, 'Delete'),
        h('span', { role: 'button', tabindex: 0, class: 'preview-action' }, 'Preview'),
      ],
    },
  ]

  let router: Router
  let rowTable: ReturnType<typeof mount>

  const mountRows = async (withRowTo = true) => {
    router = makeRouter()
    router.push('/items')
    await router.isReady()
    rowTable = mount(DataTable, {
      props: {
        columns: rowColumns,
        data: [{ id: 'r1', name: 'Annual report', size: 12 }],
        totalCount: 1,
        ...(withRowTo ? { rowTo: (row: { original: Row }) => ({ name: 'Item', params: { id: row.original.id } }) } : {}),
      },
      global: { plugins: [router] },
      attachTo: document.body,
    })
    await flushPromises()
  }

  const cell = (index: number) => rowTable.findAll('tbody td')[index]!

  afterEach(() => {
    rowTable?.unmount()
    vi.restoreAllMocks()
  })

  it('gives each row a real link in its first cell, named by that cell', async () => {
    await mountRows()

    const link = cell(0).get('a')
    expect(link.attributes('href')).toBe('/items/r1')
    expect(link.text()).toBe('Annual report')
    ;(link.element as HTMLAnchorElement).focus()
    expect(document.activeElement).toBe(link.element)
  })

  it('opens the row in the same tab on a plain click anywhere else in the row', async () => {
    await mountRows()

    await cell(1).trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/items/r1')
  })

  it.each([
    ['a ctrl click', (element: HTMLElement) => element.dispatchEvent(new MouseEvent('click', { bubbles: true, ctrlKey: true }))],
    ['a cmd click', (element: HTMLElement) => element.dispatchEvent(new MouseEvent('click', { bubbles: true, metaKey: true }))],
    ['a middle click', (element: HTMLElement) => element.dispatchEvent(new MouseEvent('auxclick', { bubbles: true, button: 1 }))],
  ])('opens the row in a new tab on %s elsewhere in the row', async (_, click) => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null)
    await mountRows()

    click(cell(1).element as HTMLElement)
    await flushPromises()

    expect(open).toHaveBeenCalledWith('/items/r1', '_blank', 'noopener')
    expect(router.currentRoute.value.fullPath).toBe('/items')
  })

  it.each([
    ['a native button', '.delete-action'],
    ['an element with role=button', '.preview-action'],
  ])('leaves a click that starts on %s in the row alone', async (_, selector) => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null)
    await mountRows()

    await rowTable.get(selector).trigger('click')
    rowTable.get(selector).element.dispatchEvent(new MouseEvent('click', { bubbles: true, metaKey: true }))
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/items')
    expect(open).not.toHaveBeenCalled()
  })

  it('renders plain rows with no link when nothing opens them', async () => {
    await mountRows(false)

    expect(rowTable.find('tbody a').exists()).toBe(false)
    await cell(1).trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/items')
  })

  it('leaves the column it is told to fill without a width, so it takes the slack', () => {
    const sized: ColumnDef<Row>[] = [
      { accessorKey: 'name', header: 'Name', size: 120 },
      { accessorKey: 'size', header: 'Size', size: 80 },
    ]
    wrapper = mount(DataTable, {
      props: { columns: sized, data: [], totalCount: 0, fillColumn: 'name' },
      global: { plugins: [makeRouter()] },
    })

    expect(wrapper.findAll('th').map(th => (th.element as HTMLElement).style.width)).toEqual(['', '80px'])
    wrapper.unmount()
  })
})
