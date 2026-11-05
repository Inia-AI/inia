<script setup lang="ts">
import { nextTick, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  modelValue: string
  label: string
  placeholder?: string
  disabled?: boolean
  loading?: boolean
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'submit'): void
}>()

const { t: $t } = useI18n()

const textareaRef = ref<HTMLTextAreaElement | null>(null)

const onInput = (e: Event) => {
  const el = e.target as HTMLTextAreaElement
  emit('update:modelValue', el.value)
  el.style.height = 'auto'
  el.style.height = Math.min(el.scrollHeight, 120) + 'px'
}

const resizeToContent = () => {
  const el = textareaRef.value
  if (!el) { return }
  el.style.height = 'auto'
  if (el.value) {
    el.style.height = Math.min(el.scrollHeight, 120) + 'px'
  }
}

// Programmatic v-model updates (e.g. Explore -> Refine pipe) don't fire `input`,
// so the auto-grow in onInput never runs. Watch covers prop changes while mounted.
watch(() => props.modelValue, async () => {
  await nextTick()
  resizeToContent()
})

// Reka's TabsContent unmounts inactive tabs. When the Refine tab activates with a
// prepopulated value (Explore -> Refine), the watch above fires before this
// component mounts. Resize again on mount so the box opens at the right height.
onMounted(() => {
  resizeToContent()
})

const onKeydown = (e: KeyboardEvent) => {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    emit('submit')
  }
}
</script>

<template>
  <div class="chat-input">
    <div class="chat-input-row">
      <textarea
        ref="textareaRef"
        :value="modelValue"
        :disabled="disabled"
        :aria-label="label"
        :placeholder="placeholder"
        class="chat-textarea"
        rows="1"
        @input="onInput"
        @keydown="onKeydown"
      />
      <button
        type="button"
        class="chat-send"
        :aria-label="$t('shared.aiPanel.send')"
        :title="$t('shared.aiPanel.send')"
        :disabled="disabled || !modelValue.trim()"
        @click="$emit('submit')"
      >
        <svg v-if="!loading" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="22" y1="2" x2="11" y2="13" /><polygon points="22 2 15 22 11 13 2 9 22 2" /></svg>
        <span v-else class="chat-send-spinner" />
      </button>
    </div>
    <slot />
  </div>
</template>

<style scoped>
.chat-input { margin-top: 0; }

.chat-input-row {
  display: flex;
  align-items: flex-end;
  gap: 8px;
  padding: 6px 6px 6px 12px;
  border: 1px solid var(--stroke);
  border-radius: 8px;
  background: transparent;
  transition: border-color 0.15s ease;
}

.chat-input-row:focus-within {
  border-color: var(--stroke-hover);
}

.chat-textarea {
  flex: 1;
  padding: 4px 0;
  border: none;
  font-size: 13px;
  line-height: 1.5;
  resize: none;
  background: transparent;
  color: var(--text-primary);
  outline: none;
  min-height: 24px;
  max-height: 120px;
}

.chat-textarea::placeholder { color: var(--text-muted); }

.chat-send {
  flex-shrink: 0;
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--text-muted);
  cursor: pointer;
  transition: color 0.15s ease;
}

.chat-send:hover:not(:disabled) {
  color: var(--text-primary);
}

.chat-send:disabled {
  opacity: 0.25;
  cursor: not-allowed;
}

.chat-send svg { width: 16px; height: 16px; }

.chat-send-spinner {
  width: 14px; height: 14px;
  border: 2px solid var(--stroke);
  border-top-color: var(--text-secondary);
  border-radius: 50%;
  animation: spin 0.6s linear infinite;
}

@keyframes spin { to { transform: rotate(360deg); } }
</style>
