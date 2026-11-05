<script setup lang="ts">
import { vSafeHtml } from '@/lib/safeHtml'

defineProps<{
  content: string | null | undefined
}>()
</script>

<template>
  <div class="markdown-text" v-safe-html.markdown="content"></div>
</template>

<style scoped>
.markdown-text {
  color: var(--text-primary);
  word-break: break-word;
  line-height: 1.5;
}
.markdown-text :deep(p) {
  margin: 0 0 1em 0;
}
.markdown-text :deep(p:last-child) {
  margin-bottom: 0;
}
.markdown-text :deep(strong),
.markdown-text :deep(b) {
  font-weight: 600;
  color: var(--text-primary);
}
.markdown-text :deep(ul),
.markdown-text :deep(ol) {
  margin: 0.5em 0;
  padding-left: 1.5em;
}
.markdown-text :deep(li) {
  margin: 0.25em 0;
}
.markdown-text :deep(li)::marker {
  color: var(--text-secondary);
}
.markdown-text :deep(h1),
.markdown-text :deep(h2),
.markdown-text :deep(h3),
.markdown-text :deep(h4) {
  margin: 1em 0 0.5em 0;
  font-weight: 600;
}
.markdown-text :deep(h1) {
  font-size: 1.5em;
}
.markdown-text :deep(h2) {
  font-size: 1.3em;
}
.markdown-text :deep(h3) {
  font-size: 1.15em;
}
.markdown-text :deep(h4) {
  font-size: 1em;
}
.markdown-text :deep(a) {
  color: var(--accent-indigo-from);
  text-decoration: underline;
}
.markdown-text :deep(code) {
  background: var(--bg-2);
  padding: 0.2em 0.4em;
  border-radius: 3px;
  font-family: monospace;
  font-size: 0.9em;
}
.markdown-text :deep(pre) {
  background: var(--bg-2);
  padding: 1em;
  border-radius: var(--radius-button);
  overflow-x: auto;
  margin: 1em 0;
}
.markdown-text :deep(pre code) {
  background: none;
  padding: 0;
}
.markdown-text :deep(blockquote) {
  margin: 1em 0;
  padding-left: 1em;
  border-left: 3px solid var(--stroke);
  color: var(--text-secondary);
  font-style: italic;
}
.markdown-text :deep(hr) {
  margin: 1.5em 0;
  border: none;
  border-top: 1px solid var(--stroke);
}
.markdown-text :deep(table) {
  border-collapse: collapse;
  margin: 0.5em 0;
}
.markdown-text :deep(th),
.markdown-text :deep(td) {
  border: 1px solid var(--stroke);
  padding: 4px 8px;
  text-align: left;
}
.markdown-text :deep(th) {
  background: var(--bg-2);
  font-weight: 600;
}
</style>
