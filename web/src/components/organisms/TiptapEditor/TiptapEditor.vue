<script lang="ts" setup>
import { watch, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { useEditor, EditorContent } from '@tiptap/vue-3'
import { markTextRuns, reportHtmlOf, withoutForeignStyling } from './reportHtml'
import { reportEditorExtensions } from './reportMarkup'

const { t: $t } = useI18n()

const TOOLBAR = 'shared.components.richTextToolbar'

const toolbarName = (key: string) => {
  const name = $t(`${TOOLBAR}.${key}`)
  return { title: name, 'aria-label': name }
}

const props = defineProps<{
  modelValue: string
  label: string
  autofocus?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

let htmlInEditor: string | null = null

const editor = useEditor({
  content: markTextRuns(props.modelValue),
  autofocus: props.autofocus,
  editorProps: {
    attributes: { role: 'textbox', 'aria-label': props.label, 'aria-multiline': 'true' },
    transformPastedHTML: html => markTextRuns(withoutForeignStyling(html)),
  },
  extensions: reportEditorExtensions,
  onUpdate: ({ editor, transaction }) => {
    const onlyTheEditorNormalizedTheContent = !transaction.docChanged
    if (onlyTheEditorNormalizedTheContent) return
    htmlInEditor = reportHtmlOf(editor)
    emit('update:modelValue', htmlInEditor)
  },
})

watch(
  () => props.modelValue,
  newValue => {
    if (editor.value && newValue !== htmlInEditor) {
      editor.value.commands.setContent(markTextRuns(newValue), { emitUpdate: false })
      htmlInEditor = newValue
    }
  },
)

onBeforeUnmount(() => {
  editor.value?.destroy()
})

defineExpose({ editor })

function setLink() {
  if (!editor.value) return
  const previousUrl = editor.value.getAttributes('link').href
  const url = window.prompt('URL', previousUrl)
  if (url === null) return
  if (url === '') {
    editor.value.chain().focus().extendMarkRange('link').unsetLink().run()
    return
  }
  editor.value.chain().focus().extendMarkRange('link').setLink({ href: url }).run()
}

function addImage() {
  if (!editor.value) return
  const url = window.prompt('Image URL')
  if (url) {
    editor.value.chain().focus().setImage({ src: url }).run()
  }
}

function insertTable() {
  if (!editor.value) return
  editor.value
    .chain()
    .focus()
    .insertTable({ rows: 3, cols: 3, withHeaderRow: true })
    .run()
}
</script>

<template>
  <div v-if="editor" class="tiptap-editor">
    <div class="tiptap-toolbar">
      <div class="toolbar-group">
        <button
          type="button"
          class="toolbar-btn"
          :class="{ active: editor.isActive('bold') }"
          :aria-pressed="editor.isActive('bold')"
          v-bind="toolbarName('bold')"
          @click="editor!.chain().focus().toggleBold().run()"
        >
          B
        </button>
        <button
          type="button"
          class="toolbar-btn"
          :class="{ active: editor.isActive('italic') }"
          :aria-pressed="editor.isActive('italic')"
          v-bind="toolbarName('italic')"
          @click="editor!.chain().focus().toggleItalic().run()"
        >
          <em>I</em>
        </button>
        <button
          type="button"
          class="toolbar-btn"
          :class="{ active: editor.isActive('underline') }"
          :aria-pressed="editor.isActive('underline')"
          v-bind="toolbarName('underline')"
          @click="editor!.chain().focus().toggleUnderline().run()"
        >
          <u>U</u>
        </button>
        <button
          type="button"
          class="toolbar-btn"
          :class="{ active: editor.isActive('strike') }"
          :aria-pressed="editor.isActive('strike')"
          v-bind="toolbarName('strike')"
          @click="editor!.chain().focus().toggleStrike().run()"
        >
          <s>S</s>
        </button>
        <button
          type="button"
          class="toolbar-btn"
          :class="{ active: editor.isActive('code') }"
          :aria-pressed="editor.isActive('code')"
          v-bind="toolbarName('code')"
          @click="editor!.chain().focus().toggleCode().run()"
        >
          &lt;/&gt;
        </button>
      </div>

      <div class="toolbar-separator" />

      <div class="toolbar-group">
        <button
          type="button"
          class="toolbar-btn"
          :class="{ active: editor.isActive('bulletList') }"
          :aria-pressed="editor.isActive('bulletList')"
          v-bind="toolbarName('bulletList')"
          @click="editor!.chain().focus().toggleBulletList().run()"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="9" y1="6" x2="20" y2="6" /><line x1="9" y1="12" x2="20" y2="12" /><line x1="9" y1="18" x2="20" y2="18" />
            <circle cx="4" cy="6" r="1.5" fill="currentColor" /><circle cx="4" cy="12" r="1.5" fill="currentColor" /><circle cx="4" cy="18" r="1.5" fill="currentColor" />
          </svg>
        </button>
        <button
          type="button"
          class="toolbar-btn"
          :class="{ active: editor.isActive('orderedList') }"
          :aria-pressed="editor.isActive('orderedList')"
          v-bind="toolbarName('orderedList')"
          @click="editor!.chain().focus().toggleOrderedList().run()"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="10" y1="6" x2="20" y2="6" /><line x1="10" y1="12" x2="20" y2="12" /><line x1="10" y1="18" x2="20" y2="18" />
            <text x="2" y="8" font-size="8" fill="currentColor" stroke="none" font-weight="bold">1</text>
            <text x="2" y="14" font-size="8" fill="currentColor" stroke="none" font-weight="bold">2</text>
            <text x="2" y="20" font-size="8" fill="currentColor" stroke="none" font-weight="bold">3</text>
          </svg>
        </button>
      </div>

      <div class="toolbar-separator" />

      <div class="toolbar-group">
        <button
          type="button"
          class="toolbar-btn"
          :class="{ active: editor.isActive('link') }"
          v-bind="toolbarName('link')"
          @click="setLink"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71" />
            <path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" />
          </svg>
        </button>
        <button
          type="button"
          class="toolbar-btn"
          v-bind="toolbarName('image')"
          @click="addImage"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
            <circle cx="8.5" cy="8.5" r="1.5" />
            <polyline points="21 15 16 10 5 21" />
          </svg>
        </button>
        <button
          type="button"
          class="toolbar-btn"
          v-bind="toolbarName('table')"
          @click="insertTable"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <rect x="3" y="3" width="18" height="18" rx="2" />
            <line x1="3" y1="9" x2="21" y2="9" />
            <line x1="3" y1="15" x2="21" y2="15" />
            <line x1="9" y1="3" x2="9" y2="21" />
            <line x1="15" y1="3" x2="15" y2="21" />
          </svg>
        </button>
      </div>

      <div class="toolbar-separator" />

      <div class="toolbar-group">
        <button
          type="button"
          class="toolbar-btn"
          v-bind="toolbarName('undo')"
          :disabled="!editor.can().undo()"
          @click="editor!.chain().focus().undo().run()"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="1 4 1 10 7 10" />
            <path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10" />
          </svg>
        </button>
        <button
          type="button"
          class="toolbar-btn"
          v-bind="toolbarName('redo')"
          :disabled="!editor.can().redo()"
          @click="editor!.chain().focus().redo().run()"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="23 4 23 10 17 10" />
            <path d="M20.49 15a9 9 0 1 1-2.13-9.36L23 10" />
          </svg>
        </button>
      </div>
    </div>

    <EditorContent :editor="editor" class="tiptap-content" />
  </div>
</template>

<style scoped>
@reference "tailwindcss";

.tiptap-editor {
  border: 1px solid var(--stroke);
  border-radius: var(--radius-small);
  background: var(--bg-1);
  overflow: hidden;
}

.tiptap-toolbar {
  display: flex;
  align-items: center;
  gap: 2px;
  padding: 6px 8px;
  border-bottom: 1px solid var(--stroke);
  background: var(--bg-2);
  flex-wrap: wrap;
}

.toolbar-group {
  display: flex;
  align-items: center;
  gap: 2px;
}

.toolbar-separator {
  width: 1px;
  height: 20px;
  background: var(--stroke);
  margin: 0 4px;
}

.toolbar-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 28px;
  border: none;
  border-radius: 4px;
  background: transparent;
  color: var(--text-primary);
  cursor: pointer;
  font-size: 13px;
  font-weight: 600;
  transition: all 0.15s ease;
}

.toolbar-btn:hover:not(:disabled) {
  background: var(--bg-1);
}

.toolbar-btn.active {
  background: var(--primary);
  color: var(--primary-foreground);
}

.toolbar-btn:disabled {
  opacity: 0.3;
  cursor: default;
}

.tiptap-content {
  min-height: 200px;
  padding: 12px 16px;
}

/* Style the ProseMirror editor content */
.tiptap-content :deep(.tiptap) {
  outline: none;
  min-height: 180px;
  line-height: 1.7;
  font-size: 16px;
  color: var(--text-primary);
}

.tiptap-content :deep(.tiptap p) {
  margin-bottom: 0.5em;
}

.tiptap-content :deep(.tiptap p:last-child) {
  margin-bottom: 0;
}

.tiptap-content :deep(.tiptap h1),
.tiptap-content :deep(.tiptap h2),
.tiptap-content :deep(.tiptap h3),
.tiptap-content :deep(.tiptap h4),
.tiptap-content :deep(.tiptap h5),
.tiptap-content :deep(.tiptap h6) {
  font-weight: 600;
  color: var(--text-primary);
  letter-spacing: -0.02em;
  line-height: 1.3;
  margin-top: 1em;
  margin-bottom: 0.5em;
}

.tiptap-content :deep(.tiptap h1) { font-size: 28px; font-weight: 700; }
.tiptap-content :deep(.tiptap h2) { font-size: 24px; font-weight: 700; }
.tiptap-content :deep(.tiptap h3) { font-size: 20px; }
.tiptap-content :deep(.tiptap h4) { font-size: 18px; }

.tiptap-content :deep(.tiptap ul),
.tiptap-content :deep(.tiptap ol) {
  margin: 0.5em 0;
  padding-left: 1.5em;
}

.tiptap-content :deep(.tiptap ul) { list-style-type: disc; }
.tiptap-content :deep(.tiptap ol) { list-style-type: decimal; }

.tiptap-content :deep(.tiptap li) {
  margin-bottom: 0.25em;
}

.tiptap-content :deep(.tiptap a) {
  color: var(--accent-indigo-from);
  text-decoration: underline;
}

.tiptap-content :deep(.tiptap strong) {
  font-weight: 600;
}

.tiptap-content :deep(.tiptap code) {
  background: var(--bg-2);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.9em;
  font-family: 'Monaco', 'Menlo', 'Consolas', monospace;
  color: var(--accent-indigo-from);
}

.tiptap-content :deep(.tiptap pre) {
  background: var(--bg-2);
  padding: var(--spacing-sm);
  border-radius: var(--radius-small);
  overflow-x: auto;
  margin: 0.5em 0;
}

.tiptap-content :deep(.tiptap pre code) {
  background: none;
  padding: 0;
  font-size: 13px;
}

.tiptap-content :deep(.tiptap blockquote) {
  border-left: 3px solid var(--accent-indigo-from);
  padding-left: var(--spacing-sm);
  margin: 1em 0;
  color: var(--text-secondary);
  font-style: italic;
}

.tiptap-content :deep(.tiptap hr) {
  border: none;
  border-top: 1px solid var(--stroke);
  margin: var(--spacing-md) 0;
}

/* Table styles inside editor */
.tiptap-content :deep(.tiptap table) {
  border-collapse: collapse;
  width: 100%;
  margin: 0.5em 0;
}

.tiptap-content :deep(.tiptap th),
.tiptap-content :deep(.tiptap td) {
  border: 1px solid var(--stroke);
  padding: 8px 12px;
  text-align: left;
  min-width: 80px;
}

.tiptap-content :deep(.tiptap th) {
  background: var(--bg-2);
  font-weight: 600;
}

/* Selected cell highlight */
.tiptap-content :deep(.tiptap .selectedCell) {
  background: rgba(92, 124, 250, 0.1);
}

/* Table resize handle */
.tiptap-content :deep(.tiptap .column-resize-handle) {
  position: absolute;
  right: -2px;
  top: 0;
  bottom: -2px;
  width: 4px;
  background-color: var(--primary);
  pointer-events: none;
}

.tiptap-content :deep(.tiptap .tableWrapper) {
  overflow-x: auto;
}

/* Image styling */
.tiptap-content :deep(.tiptap img) {
  max-width: 100%;
  height: auto;
  border-radius: var(--radius-small);
}
</style>
