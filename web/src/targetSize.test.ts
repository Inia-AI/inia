import { describe, expect, it } from 'vitest'
import { buttonVariants, type ButtonVariants } from '@/components/ui/button'
import { tableActionButton } from '@/tables/tableActionButton'
import { MIN_TARGET_PX, targetBox, undersizedTargets } from '@/test/helpers/targetSizes'

const BUTTON_SIZES: NonNullable<ButtonVariants['size']>[] = ['default', 'sm', 'lg', 'icon']

describe('pointer targets', () => {
  it(`give every icon button, badge button and sized button at least ${MIN_TARGET_PX}px each way`, () => {
    expect(undersizedTargets()).toEqual([])
  })

  it.each(BUTTON_SIZES)('keep the %s Button size at least 24px tall', size => {
    expect(targetBox(buttonVariants({ size }).split(/\s+/)).height).toBeGreaterThanOrEqual(MIN_TARGET_PX)
  })

  it('keep the table row action button at least 24px each way around its icon', () => {
    const button = tableActionButton({ label: 'Edit', icon: 'pencil', onActivate: () => {} })
    const classes = String(button.props?.class).split(/\s+/).filter(Boolean)

    expect(targetBox(classes, { iconOnly: true })).toEqual({ height: MIN_TARGET_PX, width: MIN_TARGET_PX })
  })
})
