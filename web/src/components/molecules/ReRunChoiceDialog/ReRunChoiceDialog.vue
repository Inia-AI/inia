<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { RotateCw, Pencil } from '@lucide/vue'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'

interface Props {
  modelValue: boolean
}

defineProps<Props>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  restart: []
  edit: []
}>()

const { t: $t } = useI18n()

const close = () => emit('update:modelValue', false)

const onRestart = () => {
  emit('restart')
  close()
}

const onEdit = () => {
  emit('edit')
  close()
}

// Activate on Enter / Space — replicates native button keyboard behaviour
// since we render the cards as divs (more on that below).
const onKeyActivate = (handler: () => void) => (event: KeyboardEvent) => {
  if (event.key === 'Enter' || event.key === ' ') {
    event.preventDefault()
    handler()
  }
}
</script>

<template>
  <Dialog
    :open="modelValue"
    @update:open="value => emit('update:modelValue', value)"
  >
    <!-- Cards instead of footer buttons: each choice is its own clickable
         row with an icon, label, and one-line explanation. Vertical stack
         gives the two affirmative actions equal weight visually while the
         primary (Restart) carries a gradient and a stronger affordance.
         Dismiss is handled by the built-in X / Esc / overlay.
         The cards are role="button" divs rather than <button> elements
         because DialogContent's "modern" theme stamps a global
         `button[type="button"] { background: transparent !important; ...}`
         rule on every descendant button — that strips our gradient and
         flattens the two cards into identical outlines, defeating the
         entire point of having distinct choices. -->
    <DialogContent class="sm:max-w-lg p-0">
      <DialogHeader class="px-6 pt-6 pb-4">
        <DialogTitle class="text-lg font-semibold">
          {{ $t('shared.reRunChoice.title') }}
        </DialogTitle>
        <DialogDescription class="text-sm text-muted-foreground">
          {{ $t('shared.reRunChoice.description') }}
        </DialogDescription>
      </DialogHeader>

      <div class="px-6 pb-6 flex flex-col gap-3">
        <div
          role="button"
          tabindex="0"
          class="rerun-choice rerun-choice--primary"
          @click="onRestart"
          @keydown="onKeyActivate(onRestart)"
        >
          <span class="rerun-choice__icon">
            <RotateCw class="w-5 h-5" />
          </span>
          <span class="rerun-choice__body">
            <span class="rerun-choice__label">
              {{ $t('shared.reRunChoice.restart') }}
            </span>
            <span class="rerun-choice__hint">
              {{ $t('shared.reRunChoice.restartHint') }}
            </span>
          </span>
        </div>

        <div
          role="button"
          tabindex="0"
          class="rerun-choice"
          @click="onEdit"
          @keydown="onKeyActivate(onEdit)"
        >
          <span class="rerun-choice__icon">
            <Pencil class="w-5 h-5" />
          </span>
          <span class="rerun-choice__body">
            <span class="rerun-choice__label">
              {{ $t('shared.reRunChoice.editInputs') }}
            </span>
            <span class="rerun-choice__hint">
              {{ $t('shared.reRunChoice.editInputsHint') }}
            </span>
          </span>
        </div>
      </div>
    </DialogContent>
  </Dialog>
</template>

<style scoped>
.rerun-choice {
  display: flex;
  align-items: flex-start;
  gap: var(--spacing-md);
  padding: var(--spacing-md);
  text-align: left;
  background: var(--bg-2);
  border: 1px solid var(--stroke);
  border-radius: var(--radius-card);
  color: var(--text-primary);
  cursor: pointer;
  transition: transform 0.15s ease, border-color 0.15s ease,
    background 0.15s ease, box-shadow 0.15s ease;
  user-select: none;
}

.rerun-choice:hover {
  border-color: var(--accent-indigo-from);
  background: rgba(26, 56, 80, 0.06);
  transform: translateY(-1px);
  box-shadow: 0 6px 16px rgba(0, 0, 0, 0.18);
}

.rerun-choice:focus-visible {
  outline: none;
  border-color: var(--accent-indigo-from);
  box-shadow: 0 0 0 3px rgba(26, 56, 80, 0.35);
}

.rerun-choice--primary {
  border-color: transparent;
  background: var(--primary);
  color: var(--primary-foreground);
}

.rerun-choice--primary:hover {
  border-color: transparent;
  background: var(--primary);
  box-shadow: 0 8px 20px rgba(26, 56, 80, 0.35);
}

.rerun-choice__icon {
  flex: 0 0 auto;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  border-radius: 9999px;
  background: rgba(255, 255, 255, 0.18);
}

.rerun-choice:not(.rerun-choice--primary) .rerun-choice__icon {
  background: var(--bg-1);
  color: var(--accent-indigo-from);
  border: 1px solid var(--stroke);
}

.rerun-choice__body {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  flex: 1;
}

.rerun-choice__label {
  font-size: 14px;
  font-weight: 600;
  line-height: 1.3;
}

.rerun-choice__hint {
  font-size: 13px;
  line-height: 1.4;
}

.rerun-choice:not(.rerun-choice--primary) .rerun-choice__hint {
  color: var(--text-secondary);
}
</style>
