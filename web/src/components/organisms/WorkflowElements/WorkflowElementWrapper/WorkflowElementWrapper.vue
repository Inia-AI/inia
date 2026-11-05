<script lang="ts" setup>
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import { computed, PropType, useTemplateRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useCanAskAi } from '@/composables/useCapabilities'

const { t: $t } = useI18n()

const { actions, showActions } = defineProps({
  actions: {
    type: Array as PropType<string[]>,
  },
  showActions: {
    type: Boolean,
  },
})

const $emits = defineEmits(['edit', 'copy', 'terminal', 'remove', 'trace'])

const onTraceClick = () => {
  $emits('trace')
}

const onEditClick = (action: 'edit' | 'editHeading') => () => {
  $emits('edit', action)
}

const onCopyClick = () => {
  $emits('copy')
}

const onTerminalClick = () => {
  $emits('terminal')
}

const onRemoveClick = () => {
  $emits('remove')
}

const actionMap = [
  {
    name: 'trace',
    click: onTraceClick,
    icon: 'gem',
    labelKey: 'shared.tracer.title',
    isAiButton: true,
  },
  {
    name: 'edit',
    click: onEditClick('edit'),
    icon: 'pen-to-square',
    labelKey: 'shared.components.workflowElement.actions.edit',
  },
  {
    name: 'editHeading',
    click: onEditClick('editHeading'),
    icon: 'pen-to-square',
    labelKey: 'shared.components.workflowElement.actions.editHeading',
  },
  {
    name: 'copy',
    click: onCopyClick,
    icon: 'copy',
    labelKey: 'shared.components.workflowElement.actions.copy',
  },
  {
    name: 'terminal',
    click: onTerminalClick,
    icon: 'terminal',
    labelKey: 'shared.components.workflowElement.actions.terminal',
  },
  {
    name: 'remove',
    click: onRemoveClick,
    icon: 'trash',
    labelKey: 'shared.components.workflowElement.actions.remove',
  },
]

const canAskAi = useCanAskAi()

const wrapperActions = computed(() => {
  const shown = actions?.length ? actions.flatMap(x => actionMap.find(y => y.name === x) ?? []) : actionMap
  return canAskAi.value ? shown : shown.filter(action => action.name !== 'trace')
})

const actionStrip = useTemplateRef<HTMLElement>('actionStrip')

const focusAction = (name: string) => {
  actionStrip.value?.querySelector<HTMLElement>(`[data-action="${name}"]`)?.focus()
}

defineExpose({ focusAction })
</script>

<template>
  <div class="group flex relative w-full">
    <div
      v-if="showActions"
      ref="actionStrip"
      class="icons-container flex flex-col gap-2 opacity-0 absolute -left-[32px] md:-left-[38px] group-hover:opacity-100 group-hover:translate-x-0 group-focus-within:opacity-100 group-focus-within:translate-x-0 translate-x-full transition-all duration-500 ease-out"
    >
      <button
        v-for="act in wrapperActions"
        :key="act.name"
        type="button"
        :class="act.isAiButton ? 'ai-trace-button' : 'element-action'"
        :data-action="act.name"
        :aria-label="$t(act.labelKey)"
        @click="act.click"
      >
        <!-- Gemini-style dual sparkle SVG -->
        <svg
          v-if="act.isAiButton"
          class="w-4 h-4"
          viewBox="0 0 28 24"
          fill="currentColor"
        >
          <!-- Main large sparkle -->
          <path d="M9 1C9 7 4 12 0 12C4 12 9 17 9 23C9 17 14 12 20 12C14 12 9 7 9 1Z" />
          <!-- Small sparkle top-right -->
          <path d="M22 0C22 3 19.5 5 17 5C19.5 5 22 7 22 10C22 7 24.5 5 27 5C24.5 5 22 3 22 0Z" opacity="0.85" />
        </svg>
        <IniaIcon v-else :name="act.icon" class="text-md md:text-xl" />
      </button>
    </div>
    <div
      class="flex flex-col w-full p-1 workflow-content-wrapper"
      :class="showActions && 'has-actions'"
    >
      <slot />
    </div>
  </div>
</template>

<style scoped>
.workflow-content-wrapper.has-actions {
  border-radius: var(--radius-small);
  transition: all 0.2s ease;
  border-left: 3px solid transparent;
  padding-left: 10px;
}

.group:hover .workflow-content-wrapper.has-actions {
  border-left: 3px solid var(--primary);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
  transform: translateY(-2px);
}

/* AI Trace Button - Gemini-style gradient */
.ai-trace-button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  background: var(--primary);
  border-radius: 6px;
  cursor: pointer;
  transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1),
              box-shadow 0.3s cubic-bezier(0.4, 0, 0.2, 1),
              opacity 0.3s ease;
  box-shadow: 0 2px 6px rgba(26, 56, 80, 0.25);
  color: var(--primary-foreground);
  transform: scale(1);
}

.ai-trace-button:hover {
  transform: scale(1.05);
  box-shadow: 0 3px 10px rgba(26, 56, 80, 0.4);
}

.ai-trace-button:active {
  transform: scale(0.98);
  transition: transform 0.1s ease;
}

.element-action {
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}

.ai-trace-button:focus-visible,
.element-action:focus-visible {
  outline: 2px solid var(--ring);
  outline-offset: 2px;
  border-radius: 6px;
}

/* Extend hover area to bridge gap between buttons and content */
.icons-container::after {
  content: '';
  position: absolute;
  top: 0;
  right: -16px;
  width: 16px;
  height: 100%;
}
</style>
