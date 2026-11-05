<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { Button } from '@/components/ui/button'
import IniaIcon from '@/components/atoms/IniaIcon/IniaIcon.vue'

interface Props {
  jobId?: string | null
  variant?: 'icon' | 'default'
  subject?: string
}

const props = withDefaults(defineProps<Props>(), {
  jobId: null,
  variant: 'icon',
})

const { t: $t } = useI18n()
const router = useRouter()

const title = computed(() =>
  props.subject ? $t('shared.openDocuments.openFor', { subject: props.subject }) : $t('shared.openDocuments.title'),
)

const canRender = computed(
  () => typeof props.jobId === 'string' && props.jobId.length > 0,
)

const onClick = (event: MouseEvent) => {
  event.stopPropagation()
  if (!canRender.value) return
  const { href } = router.resolve({
    name: 'Documents',
    query: { jobId: props.jobId as string },
  })
  window.open(href, '_blank', 'noopener,noreferrer')
}
</script>

<template>
  <Button
    v-if="canRender"
    variant="outline"
    size="sm"
    :title="title"
    :aria-label="props.variant === 'icon' ? title : undefined"
    :class="[
      'open-documents-btn',
      props.variant === 'icon' ? 'open-documents-btn--icon' : null,
    ]"
    @click="onClick"
  >
    <IniaIcon name="file-lines" classes="w-4 h-4" />
    <span v-if="props.variant === 'default'">
      {{ $t('shared.openDocuments.label') }}
    </span>
  </Button>
</template>

<style scoped>
.open-documents-btn {
  transition: color 150ms ease, background-color 150ms ease;
}

.open-documents-btn--icon {
  padding-left: 0.5rem;
  padding-right: 0.5rem;
  width: 2rem;
  flex-shrink: 0;
}
</style>
