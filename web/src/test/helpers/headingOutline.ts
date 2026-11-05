import { camelize, capitalize } from 'vue'
import { directoryOf, joinPath, SOURCES, sourceOf, sourcePaths } from './sourceFiles'
import {
  boundExpression,
  directive,
  literalAttribute,
  NODE_ELEMENT,
  sfcOf,
  staticAttribute,
  TAG_SLOT,
  TAG_TEMPLATE,
  type TemplateNode,
  templateOf,
} from './templateAst'

const ROUTE_FILES = /^(plugins\/router\/index|modules\/[^/]+\/routes)\.ts$/

const HEADING_TAG = /^h([1-6])$/
const OVERLAY_CONTENT =
  /^(Dialog|AlertDialog|Sheet|Drawer|Popover|Tooltip|HoverCard|DropdownMenu|ContextMenu|Menubar|Select|Combobox)(Content|Portal)$/
const RENDERED_ONLY_WHEN_OPEN = /^(Tabs|Accordion|Collapsible)Content$/
const REKA_ACCORDION_HEADER_LEVEL = 3
const ARIA_DEFAULT_HEADING_LEVEL = 2

interface Condition {
  scope: number
  branch: number
}

export interface Heading {
  level: number
  where: string
  conditions: Condition[]
}

export type RenderNode =
  | { kind: 'heading'; level: number; where: string }
  | { kind: 'group'; optional: boolean; children: RenderNode[] }
  | { kind: 'chain'; branches: RenderNode[][]; exhaustive: boolean }

interface SlotContent {
  nodes: TemplateNode[]
  file: string
  slots: Map<string, SlotContent>
}

interface WalkContext {
  file: string
  slots: Map<string, SlotContent>
  stack: string[]
}

const parsedFiles = new Map<string, { template: TemplateNode[]; imports: Map<string, string> }>()

const toPascal = (tag: string) => capitalize(camelize(tag))

const ALIASES: [string, string][] = [
  ['@/', ''],
  ['@atoms/', 'components/atoms'],
  ['@layouts/', 'components/layouts'],
  ['@ui/', 'components/ui'],
]

const resolveSpecifier = (from: string, specifier: string): string | null => {
  const alias = ALIASES.find(([prefix]) => specifier.startsWith(prefix))
  const base = alias
    ? joinPath(alias[1], specifier.slice(alias[0].length))
    : specifier.startsWith('.')
      ? joinPath(directoryOf(from), specifier)
      : null
  if (base === null) return null
  return [base, `${base}.ts`, `${base}.vue`, joinPath(base, 'index.ts')].find(path => SOURCES.has(path) && /\.(ts|vue)$/.test(path)) ?? null
}

const barrelExport = (barrel: string, name: string, seen = new Set<string>()): string | null => {
  if (seen.has(barrel)) return null
  seen.add(barrel)
  const source = sourceOf(barrel)
  for (const match of source.matchAll(/export\s*\{([^}]*)\}\s*from\s*['"]([^'"]+)['"]/g)) {
    for (const part of match[1].split(',')) {
      const [original, alias] = part.trim().split(/\s+as\s+/)
      if ((alias ?? original) !== name) continue
      const target = resolveSpecifier(barrel, match[2])
      if (!target) return null
      if (target.endsWith('.vue') && original === 'default') return target
      if (target.endsWith('.ts')) return barrelExport(target, original, seen)
    }
  }
  for (const match of source.matchAll(/export\s*\*\s*from\s*['"]([^'"]+)['"]/g)) {
    const target = resolveSpecifier(barrel, match[1])
    const found = target?.endsWith('.ts') ? barrelExport(target, name, seen) : null
    if (found) return found
  }
  return null
}

const importsOf = (file: string, script: string) => {
  const imports = new Map<string, string>()
  for (const match of script.matchAll(/import\s+(\w+)\s+from\s+['"]([^'"]+)['"]/g)) {
    const target = resolveSpecifier(file, match[2])
    imports.set(match[1], target ?? `package:${match[2]}`)
  }
  for (const match of script.matchAll(/import\s+(?:type\s+)?\{([^}]*)\}\s*from\s*['"]([^'"]+)['"]/g)) {
    const target = resolveSpecifier(file, match[2])
    for (const part of match[1].split(',')) {
      const [original, alias] = part.trim().replace(/^type\s+/, '').split(/\s+as\s+/)
      if (!original) continue
      const local = alias ?? original
      const component = target?.endsWith('.ts') ? barrelExport(target, original) : null
      imports.set(local, component ?? `package:${match[2]}#${original}`)
    }
  }
  return imports
}

const loadComponent = (file: string) => {
  const cached = parsedFiles.get(file)
  if (cached) return cached
  const descriptor = sfcOf(file)
  const script = `${descriptor.script?.content ?? ''}\n${descriptor.scriptSetup?.content ?? ''}`
  const loaded = { template: templateOf(file), imports: importsOf(file, script) }
  parsedFiles.set(file, loaded)
  return loaded
}

const slotNameOf = (node: TemplateNode) => {
  const slotDirective = directive(node, 'slot')
  return slotDirective ? (slotDirective.arg?.content ?? 'default') : null
}

const locate = (file: string, node: TemplateNode) => {
  const text = node.loc.source.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 60)
  return `${file}:${node.loc.start.line} "${text}"`
}

const headingLevelOf = (node: TemplateNode, imports: Map<string, string>): number | null => {
  const tag = node.tag ?? ''
  const native = tag.match(HEADING_TAG)
  if (native) return Number(native[1])
  if (staticAttribute(node, 'role') === 'heading') {
    const level = literalAttribute(node, 'aria-level')
    if (level) return Number(level)
    return boundExpression(node, 'aria-level') ? NaN : ARIA_DEFAULT_HEADING_LEVEL
  }
  const source = imports.get(toPascal(tag)) ?? ''
  if (source.endsWith('components/ui/accordion/AccordionTrigger.vue')) {
    return Number(literalAttribute(node, 'heading-level', 'headingLevel'))
  }
  if (source === 'package:reka-ui#AccordionHeader') {
    return Number(literalAttribute(node, 'as')?.match(HEADING_TAG)?.[1] ?? REKA_ACCORDION_HEADER_LEVEL)
  }
  return null
}

const slotsOfUsage = (usage: TemplateNode, context: WalkContext) => {
  const slots = new Map<string, SlotContent>()
  const defaults: TemplateNode[] = []
  for (const child of usage.children ?? []) {
    const name = child.tagType === TAG_TEMPLATE ? slotNameOf(child) : null
    if (name) {
      slots.set(name, { nodes: child.children ?? [], file: context.file, slots: context.slots })
    } else {
      defaults.push(child)
    }
  }
  if (!slots.has('default')) slots.set('default', { nodes: defaults, file: context.file, slots: context.slots })
  return slots
}

const renderElement = (node: TemplateNode, context: WalkContext): RenderNode[] => {
  const { imports } = loadComponent(context.file)
  const tag = node.tag ?? ''
  const level = headingLevelOf(node, imports)
  if (level !== null) return [{ kind: 'heading', level, where: locate(context.file, node) }]
  if (OVERLAY_CONTENT.test(tag)) return []

  if (node.tagType === TAG_SLOT) {
    const slot = context.slots.get(staticAttribute(node, 'name') ?? 'default')
    if (slot && slot.nodes.length > 0) {
      return renderChildren(slot.nodes, { file: slot.file, slots: slot.slots, stack: context.stack })
    }
    return renderChildren(node.children ?? [], context)
  }

  const component = imports.get(toPascal(tag))
  const children = component?.endsWith('.vue') && !context.stack.includes(component)
    ? renderChildren(loadComponent(component).template, {
        file: component,
        slots: slotsOfUsage(node, context),
        stack: [...context.stack, component],
      })
    : renderChildren(
        (node.children ?? []).flatMap(child => (child.tagType === TAG_TEMPLATE && slotNameOf(child) ? (child.children ?? []) : [child])),
        context,
      )
  return RENDERED_ONLY_WHEN_OPEN.test(tag) ? [{ kind: 'group', optional: true, children }] : children
}

const renderChildren = (nodes: TemplateNode[], context: WalkContext): RenderNode[] => {
  const out: RenderNode[] = []
  let chain: Extract<RenderNode, { kind: 'chain' }> | null = null
  for (const node of nodes) {
    if (node.type !== NODE_ELEMENT) continue
    const rendered = renderElement(node, context)
    const optional = !!directive(node, 'for') || !!directive(node, 'show')
    const body: RenderNode[] = optional ? [{ kind: 'group', optional: true, children: rendered }] : rendered
    if (directive(node, 'if')) {
      chain = { kind: 'chain', branches: [body], exhaustive: false }
      out.push(chain)
    } else if (chain && (directive(node, 'else-if') || directive(node, 'else'))) {
      chain.branches.push(body)
      chain.exhaustive = !!directive(node, 'else')
    } else {
      chain = null
      out.push(...body)
    }
  }
  return out
}

export const renderTree = (file: string): RenderNode[] =>
  renderChildren(loadComponent(file).template, { file, slots: new Map(), stack: [file] })

export const routedPages = (): string[] => {
  const pages = new Set<string>()
  for (const routeFile of sourcePaths(ROUTE_FILES)) {
    for (const match of sourceOf(routeFile).matchAll(/import\(\s*['"]([^'"]+\.vue)['"]\s*\)/g)) {
      const page = resolveSpecifier(routeFile, match[1])
      if (page) pages.add(page)
    }
  }
  return [...pages].sort()
}

export const guaranteesH1 = (nodes: RenderNode[]): boolean =>
  nodes.some(node => {
    if (node.kind === 'heading') return node.level === 1
    if (node.kind === 'group') return !node.optional && guaranteesH1(node.children)
    return node.exhaustive && node.branches.every(guaranteesH1)
  })

export const flattenHeadings = (nodes: RenderNode[], conditions: Condition[] = [], counter = { next: 0 }): Heading[] =>
  nodes.flatMap(node => {
    if (node.kind === 'heading') return [{ level: node.level, where: node.where, conditions }]
    if (node.kind === 'group') {
      const scoped = node.optional ? [...conditions, { scope: counter.next++, branch: 0 }] : conditions
      return flattenHeadings(node.children, scoped, counter)
    }
    const scope = counter.next++
    return node.branches.flatMap((branch, index) => flattenHeadings(branch, [...conditions, { scope, branch: index }], counter))
  })

const exclusive = (a: Heading, b: Heading) =>
  a.conditions.some(left => b.conditions.some(right => left.scope === right.scope && left.branch !== right.branch))

const presentWhenever = (earlier: Heading, later: Heading) =>
  earlier.conditions.every(left => later.conditions.some(right => left.scope === right.scope && left.branch === right.branch))

export const headingOrderProblems = (headings: Heading[]): string[] =>
  headings.flatMap((heading, index) => {
    if (Number.isNaN(heading.level)) return [`${heading.where} takes its heading level from a value this guard cannot read`]
    const problems: string[] = []
    for (let back = index - 1; back >= 0; back--) {
      const previous = headings[back]
      if (exclusive(previous, heading)) continue
      if (heading.level === 1 && previous.level === 1) {
        problems.push(`a second h1 ${heading.where} can render with ${previous.where}`)
      }
      if (previous.level < heading.level - 1) {
        problems.push(`h${heading.level} ${heading.where} can follow h${previous.level} ${previous.where}`)
      }
      if (presentWhenever(previous, heading)) break
    }
    return problems
  })

