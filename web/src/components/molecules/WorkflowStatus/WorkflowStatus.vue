<script setup lang="ts">
import { WorkflowState } from '@/enums'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'
import IniaLoader from '@/components/atoms/IniaLoader/IniaLoader.vue'
import { describeRunFailure } from '@/utils/runFailure'
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip'

const props = defineProps({
  status: {
    type: String,
  },
  fontSize: {
    type: String,
    default: 'text-lg',
  },
  failureKind: {
    type: String as () => string | null,
    default: null,
  },
  failureDetail: {
    type: String as () => string | null,
    default: null,
  },
})

const { t: $t } = useI18n()

// Indicator metadata per state. The Status column is icon-only in dense
// table cells, so each glyph carries the load — pick icons that read at a
// glance and pair them with a tooltip describing what the user is looking
// at. Running / ProcessingDocuments deliberately reuse the spinner (both
// are generic in-flight states); the tooltip differentiates them.
const indicator = computed<{
  kind: 'icon'
  name: string
  color: string
} | { kind: 'spinner', color: string } | null>(() => {
  switch (props.status) {
    case WorkflowState.Queued:
      // Sitting in line — clock reads as "waiting" without implying work.
      return { kind: 'icon', name: 'clock', color: 'text-info' }
    case WorkflowState.ProcessingDocuments:
    case WorkflowState.Running:
      // Active spinner gets warning amber so in-flight states pop visually
      // against the muted "queued / completed" neutrals.
      return { kind: 'spinner', color: 'text-warning' }
    case WorkflowState.Draft:
      // Both Draft and Confirmed are completion states; both read with the
      // shared `text-success` token (the design system's teal-leaning
      // green) so positive status is consistent with everywhere else
      // semantically positive in the app.
      return { kind: 'icon', name: 'circle-check', color: 'text-success' }
    case WorkflowState.Confirmed:
      return { kind: 'icon', name: 'circle-check', color: 'text-success' }
    case WorkflowState.Failed:
      return { kind: 'icon', name: 'circle-exclamation', color: 'text-error' }
    case WorkflowState.Cancelled:
      // Gray, not amber: Cancelled is a *terminal, dismissed* state — the
      // user stopped it, nothing to act on. Carbon, GitHub, and Linear all
      // converge on neutral gray for "won't continue" runs; amber would
      // wrongly imply "needs attention". `text-muted-foreground` resolves
      // to slate-500 light / cool gray dark via shadcn's --muted-foreground.
      return { kind: 'icon', name: 'ban', color: 'text-muted-foreground' }
    default:
      return null
  }
})

const failure = computed(() =>
  props.status === WorkflowState.Failed
    ? describeRunFailure($t, props.failureKind, props.failureDetail)
    : null,
)

const tooltip = computed(() => {
  if (!props.status) return ''
  if (failure.value) {
    return failure.value.body ? `${failure.value.label}. ${failure.value.body}` : failure.value.label
  }
  const key = `shared.enums.workflowStateTooltip.${props.status}`
  const message = $t(key)
  // i18n returns the key when missing — don't render a tooltip in that case.
  return message === key ? '' : message
})

// Screen-reader label: prefer the tooltip description when it exists
// (richer context like "Reading and parsing the input documents."),
// otherwise fall back to the literal state name.
const ariaLabel = computed(() => {
  if (!props.status) return ''
  const stateLabel = $t(`shared.enums.workflowState.${props.status}`)
  return tooltip.value ? `${stateLabel}: ${tooltip.value}` : stateLabel
})
</script>

<template>
  <span v-if="!indicator" />
  <TooltipProvider v-else :delay-duration="200">
    <Tooltip>
      <TooltipTrigger as-child>
        <!-- `role="img"` + `aria-label` so screen readers announce the
             workflow state when this component renders icon-only (e.g. the
             reports table status column). `tabindex="0"` makes the trigger
             focusable so keyboard users can read the tooltip too. The
             accessible name falls back to the literal state when the
             tooltip namespace doesn't have a description for it. -->
        <span
          class="inline-flex items-center justify-center"
          :class="indicator.color"
          role="img"
          tabindex="0"
          :aria-label="ariaLabel"
        >
          <IniaLoader v-if="indicator.kind === 'spinner'" class="w-6" />
          <IniaIcon
            v-else
            :name="indicator.name"
            :class="props.fontSize"
          />
        </span>
      </TooltipTrigger>
      <TooltipContent v-if="failure" class="max-w-xs">
        <p class="text-xs font-semibold">{{ failure.label }}</p>
        <p v-if="failure.body" class="text-xs">{{ failure.body }}</p>
      </TooltipContent>
      <TooltipContent v-else-if="tooltip" class="max-w-xs">
        <p class="text-xs">{{ tooltip }}</p>
      </TooltipContent>
    </Tooltip>
  </TooltipProvider>
</template>
