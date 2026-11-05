import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h, type Component } from 'vue'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter, type RouteRecordRaw } from 'vue-router'
import type { ColumnDef } from '@tanstack/vue-table'
import DataTable from '@/components/organisms/DataTable/DataTable.vue'
import { EN } from '@/test/helpers/messages'
import type { EN as OPEN_EN } from '@/assets/locales/en'

interface RowControl {
  row: number
  element: string
  role: string | null
  name: string
  focusable: boolean
  nested: boolean
}

const POINTER_LISTENERS = ['onClick', 'onDblclick', 'onMousedown', 'onPointerdown', 'onMouseup', 'onPointerup']

const INTERACTIVE_ROLES = new Set([
  'button',
  'link',
  'checkbox',
  'radio',
  'switch',
  'tab',
  'menuitem',
  'menuitemcheckbox',
  'menuitemradio',
  'option',
  'combobox',
  'textbox',
  'searchbox',
  'slider',
  'spinbutton',
])

const vueListeners = (element: Element): Record<string, unknown> | undefined => {
  const key = Object.getOwnPropertySymbols(element).find(symbol => symbol.description === '_vei')
  return key ? (element as unknown as Record<symbol, Record<string, unknown>>)[key] : undefined
}

const listensForPointer = (element: Element) => {
  const listeners = vueListeners(element)
  return !!listeners && POINTER_LISTENERS.some(name => listeners[name])
}

const nativeRole = (element: Element): string | null => {
  switch (element.tagName) {
    case 'BUTTON':
      return 'button'
    case 'A':
      return element.hasAttribute('href') ? 'link' : null
    case 'SELECT':
      return 'combobox'
    case 'TEXTAREA':
      return 'textbox'
    case 'INPUT': {
      const type = (element as HTMLInputElement).type
      if (type === 'hidden') return null
      if (type === 'checkbox' || type === 'radio') return type
      if (type === 'button' || type === 'submit' || type === 'reset') return 'button'
      return 'textbox'
    }
    default:
      return null
  }
}

const roleOf = (element: Element) => element.getAttribute('role') ?? nativeRole(element)

const isControl = (element: Element) => {
  const role = roleOf(element)
  return (role !== null && INTERACTIVE_ROLES.has(role)) || listensForPointer(element)
}

const isFocusable = (element: Element) => {
  if (element instanceof HTMLElement && element.tabIndex >= 0) return true
  return nativeRole(element) !== null && !(element as HTMLButtonElement).disabled
}

const visibleText = (element: Element): string => {
  if (element.getAttribute('aria-hidden') === 'true') return ''
  return [...element.childNodes]
    .map(node => (node.nodeType === Node.TEXT_NODE ? (node.textContent ?? '') : node instanceof Element ? visibleText(node) : ''))
    .join(' ')
}

export const accessibleName = (element: Element): string => {
  const labelledBy = element.getAttribute('aria-labelledby')
  if (labelledBy) {
    return labelledBy
      .split(/\s+/)
      .map(id => element.ownerDocument.getElementById(id)?.textContent ?? '')
      .join(' ')
      .replace(/\s+/g, ' ')
      .trim()
  }
  const label = element.getAttribute('aria-label')?.trim()
  if (label) return label
  const labels = (element as HTMLInputElement).labels
  if (labels?.length) return [...labels].map(l => l.textContent ?? '').join(' ').trim()
  const text = visibleText(element).replace(/\s+/g, ' ').trim()
  return text || (element.getAttribute('title')?.trim() ?? '')
}

const tagOf = (element: Element) =>
  `<${element.tagName.toLowerCase()}${element.className && typeof element.className === 'string' ? ` class="${element.className}"` : ''}>`

const insideAnotherControl = (element: Element, root: Element) => {
  for (let parent = element.parentElement; parent && parent !== root; parent = parent.parentElement) {
    if (isControl(parent)) return true
  }
  return false
}

const controlsIn = (root: Element, row: number): RowControl[] =>
  [...root.querySelectorAll('*')].filter(isControl).map(element => ({
    row,
    element: tagOf(element),
    role: roleOf(element),
    name: accessibleName(element),
    focusable: isFocusable(element),
    nested: insideAnotherControl(element, root),
  }))

const tableControls = (table: Element) => ({
  header: [...table.querySelectorAll('thead tr')].flatMap(tr => controlsIn(tr, -1)),
  rows: [...table.querySelectorAll('tbody tr')].map((tr, index) => controlsIn(tr, index)),
})

const rowControlProblems = (table: Element) => {
  const { header, rows } = tableControls(table)
  const problems: string[] = []
  for (const control of [...header, ...rows.flat()]) {
    const where = control.row < 0 ? 'header' : `row ${control.row + 1}`
    if (!control.role || !INTERACTIVE_ROLES.has(control.role)) {
      problems.push(`${where}: ${control.element} reacts to a click but has no control role`)
    }
    if (!control.focusable) {
      problems.push(`${where}: ${control.element} "${control.name}" takes no Tab stop`)
    }
    if (!control.name) {
      problems.push(`${where}: ${control.element} has no accessible name`)
    }
    if (control.nested) {
      problems.push(`${where}: ${control.element} "${control.name}" sits inside another control`)
    }
  }
  const rowsByName = new Map<string, { role: string | null; name: string; rows: Set<number> }>()
  for (const control of rows.flat()) {
    if (!control.name || control.nested) continue
    const key = `${control.role}|${control.name.toLowerCase()}`
    const entry = rowsByName.get(key) ?? { role: control.role, name: control.name, rows: new Set<number>() }
    rowsByName.set(key, entry)
    entry.rows.add(control.row)
  }
  for (const { role, name, rows: shared } of rowsByName.values()) {
    if (shared.size > 1) {
      problems.push(`"${name}" (${role}) names a control in rows ${[...shared].map(r => r + 1).join(', ')}`)
    }
  }
  return problems
}

const NO_PAGE = { render: () => null }

export const REPORT_PAGE_ROUTE = { name: 'WorkflowDetails', path: '/workflow-reports/:id/:action', component: NO_PAGE }

export const tablePlugins = (routes: RouteRecordRaw[] = []) => [
  createI18n({ legacy: false, locale: 'en', messages: { en: EN as typeof OPEN_EN } }),
  createRouter({ history: createMemoryHistory(), routes: [...routes, { path: '/:any(.*)*', component: NO_PAGE }] }),
]

export interface TableSetup {
  columns: ColumnDef<never>[]
  rowLinkColumn?: string
}

export interface TableCase {
  table: () => TableSetup
  rows: unknown[]
  rowTo?: (row: { original: never }) => unknown
}

const isTableModule = (module: Record<string, unknown>) =>
  typeof module.default === 'function' ||
  Object.entries(module).some(([name, value]) => name.startsWith('use') && typeof value === 'function')

export const describeRowControls = (
  modules: Record<string, Record<string, unknown>>,
  tables: Record<string, TableCase>,
  { routes = [], stubs = {} }: { routes?: RouteRecordRaw[]; stubs?: Record<string, boolean> } = {},
) => {
  const mountTable = ({ table, rows, rowTo }: TableCase) =>
    mount(
      defineComponent({
        setup() {
          const { columns, rowLinkColumn } = table()
          return () => h(DataTable as Component, { columns, data: rows, rowTo, rowLinkColumn })
        },
      }),
      { global: { plugins: tablePlugins(routes), stubs } },
    )

  describe('table row controls', () => {
    it('covers every table module in this folder', () => {
      const tableFiles = Object.entries(modules)
        .filter(([, module]) => isTableModule(module))
        .map(([file]) => file)
      expect(tableFiles.sort()).toEqual(Object.keys(tables).sort())
    })

    it.each(Object.entries(tables))('%s gives every control a role, a Tab stop and a name of its own row', (_, table) => {
      const element = mountTable(table).find('table').element
      const { rows } = tableControls(element)

      expect(rows).toHaveLength(table.rows.length)
      expect(rows.filter(controls => controls.length === 0)).toEqual([])
      expect(rowControlProblems(element)).toEqual([])
    })
  })
}
