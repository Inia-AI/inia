import { h } from 'vue'

import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'

interface TableActionButtonOptions {
  label: string
  icon: string
  class?: string
  iconClass?: string
  title?: string
  attrs?: Record<string, unknown>
  iconAttrs?: Record<string, unknown>
  onActivate: (event: MouseEvent) => void
}

export const tableActionButton = ({
  label,
  icon,
  class: className,
  iconClass,
  title = label,
  attrs,
  iconAttrs,
  onActivate,
}: TableActionButtonOptions) =>
  h(
    'button',
    {
      ...attrs,
      type: 'button',
      class: ['hit-target', 'table-action-button', className],
      'aria-label': label,
      title,
      onClick: (event: MouseEvent) => {
        event.stopPropagation()
        onActivate(event)
      },
    },
    [h(IniaIcon, { ...iconAttrs, name: icon, classes: iconClass })],
  )
