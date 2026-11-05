<script setup lang="ts">
import { computed } from 'vue'
import { Button } from '@/components/ui/button'

const props = defineProps<{
  totalCount: number
  pageSize: number
  page: number
}>()

const emit = defineEmits<{
  (e: 'pageChange', page: number): void
}>()

const totalPages = computed(() => Math.ceil(props.totalCount / props.pageSize))

const onPageChange = (newPage: number) => {
  if (newPage >= 0 && newPage < totalPages.value) {
    emit('pageChange', newPage)
  }
}
</script>

<template>
  <div class="flex items-center justify-center gap-2">
    <Button
      variant="outline"
      size="sm"
      :disabled="page === 0"
      @click="onPageChange(page - 1)"
    >
      Previous
    </Button>
    <span class="text-sm">
      Page {{ page + 1 }} of {{ totalPages }}
    </span>
    <Button
      variant="outline"
      size="sm"
      :disabled="page >= totalPages - 1"
      @click="onPageChange(page + 1)"
    >
      Next
    </Button>
  </div>
</template>
