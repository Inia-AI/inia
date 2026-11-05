import { afterEach, describe, expect, it } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ConfirmDialog from './ConfirmDialog.vue'

const settle = async () => {
  await flushPromises()
  await new Promise(resolve => setTimeout(resolve, 10))
  await flushPromises()
}

const alertDialog = () => document.querySelector<HTMLElement>('[role="alertdialog"]')

let wrapper: VueWrapper | null = null

const openConfirm = async () => {
  wrapper = mount(ConfirmDialog, {
    attachTo: document.body,
    props: {
      modelValue: true,
      title: 'Delete draft?',
      description: 'The draft and its inputs are removed.',
      cancelLabel: 'No',
      confirmLabel: 'Yes',
      'onUpdate:modelValue': (modelValue: boolean) => wrapper!.setProps({ modelValue }),
    },
  })
  await settle()
  return wrapper
}

const pressEscape = async () => {
  ;(document.activeElement ?? document.body).dispatchEvent(
    new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
  )
  await settle()
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = null
  document.body.innerHTML = ''
})

describe('ConfirmDialog', () => {
  it('opens as an alert dialog named by its title and described by its text, with focus inside', async () => {
    await openConfirm()

    const dialog = alertDialog()!
    expect(document.getElementById(dialog.getAttribute('aria-labelledby')!)!.textContent).toContain('Delete draft?')
    expect(document.getElementById(dialog.getAttribute('aria-describedby')!)!.textContent).toContain(
      'The draft and its inputs are removed.',
    )
    expect(dialog.contains(document.activeElement)).toBe(true)
  })

  it('closes on Escape, tells its owner it closed, and confirms nothing', async () => {
    const dialog = await openConfirm()

    await pressEscape()

    expect(alertDialog()).toBeNull()
    expect(dialog.emitted('update:modelValue')).toEqual([[false]])
    expect(dialog.emitted('confirm')).toBeUndefined()
  })

  it('closes from its cancel button, tells its owner it closed, and confirms nothing', async () => {
    const dialog = await openConfirm()
    const cancel = Array.from(alertDialog()!.querySelectorAll('button')).find(button => button.textContent?.trim() === 'No')!

    cancel.click()
    await settle()

    expect(alertDialog()).toBeNull()
    expect(dialog.emitted('update:modelValue')).toEqual([[false]])
    expect(dialog.emitted('confirm')).toBeUndefined()
  })

  it('stays open on a click outside, as an alert dialog should', async () => {
    const dialog = await openConfirm()
    const outside = document.createElement('h1')
    document.body.appendChild(outside)

    outside.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true, button: 0 }))
    outside.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }))
    outside.dispatchEvent(new MouseEvent('click', { bubbles: true, button: 0 }))
    await settle()

    expect(alertDialog()).not.toBeNull()
    expect(dialog.emitted('update:modelValue')).toBeUndefined()
  })
})
