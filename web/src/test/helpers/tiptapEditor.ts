import type { Editor } from '@tiptap/core'
import { expect } from 'vitest'

export const positionOf = (editor: Editor, text: string) => {
  let position = -1
  editor.state.doc.descendants((node, offset) => {
    if (position === -1 && node.isText && node.text!.includes(text)) position = offset + node.text!.indexOf(text)
  })
  expect(position, `no text "${text}" in the editor`).toBeGreaterThan(-1)
  return position
}

export const replaceWords = (editor: Editor, words: string, replacement: string) => {
  const from = positionOf(editor, words)
  editor.view.dispatch(editor.state.tr.insertText(replacement, from, from + words.length))
}
