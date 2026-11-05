import { parse, type SFCDescriptor } from 'vue/compiler-sfc'
import { sourceOf } from './sourceFiles'

export const NODE_ELEMENT = 1
export const NODE_COMMENT = 3
const NODE_ATTRIBUTE = 6
const NODE_DIRECTIVE = 7
export const TAG_SLOT = 2
export const TAG_TEMPLATE = 3

export interface TemplateProp {
  type: number
  name: string
  value?: { content: string }
  arg?: { content: string }
  exp?: { content: string }
}

export interface TemplateNode {
  type: number
  tag?: string
  tagType?: number
  props?: TemplateProp[]
  children?: TemplateNode[]
  loc: { start: { line: number }; source: string }
}

const descriptors = new Map<string, SFCDescriptor>()

export const sfcOf = (file: string) => {
  const cached = descriptors.get(file)
  if (cached) return cached
  const { descriptor } = parse(sourceOf(file), { filename: file })
  descriptors.set(file, descriptor)
  return descriptor
}

export const templateOf = (file: string) => (sfcOf(file).template?.ast?.children ?? []) as unknown as TemplateNode[]

export const directive = (node: TemplateNode, name: string) =>
  node.props?.find(prop => prop.type === NODE_DIRECTIVE && prop.name === name)

export const staticAttribute = (node: TemplateNode, ...names: string[]) =>
  node.props?.find(prop => prop.type === NODE_ATTRIBUTE && names.includes(prop.name))?.value?.content

export const boundExpression = (node: TemplateNode, name: string) =>
  node.props?.find(prop => prop.type === NODE_DIRECTIVE && prop.name === 'bind' && prop.arg?.content === name)?.exp
    ?.content

export const literalAttribute = (node: TemplateNode, ...names: string[]) =>
  staticAttribute(node, ...names) ??
  names.map(name => boundExpression(node, name)?.match(/^['"]?(\w+)['"]?$/)?.[1]).find(Boolean)

export const walkElements = (nodes: TemplateNode[], visit: (node: TemplateNode) => void) => {
  for (const node of nodes) {
    if (node.type !== NODE_ELEMENT) continue
    visit(node)
    walkElements(node.children ?? [], visit)
  }
}
